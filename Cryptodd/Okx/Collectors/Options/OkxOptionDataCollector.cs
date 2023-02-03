using System.Diagnostics;
using System.Runtime.CompilerServices;
using Cryptodd.Algorithms.Topk;
using Cryptodd.IoC;
using Cryptodd.Okx.Http;
using Cryptodd.Okx.Models;
using Cryptodd.Okx.Models.HttpResponse;
using Cryptodd.Okx.Options;
using Lamar;
using MathNet.Numerics.Random;
using Maxisoft.Utils.Objects;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Okx.Collectors.Options;

public interface IOkxOptionDataCollector : IAsyncDisposable, IDisposable
{
    Task<ISet<OkxOptionInstrumentId>> Collect(Action? onDownloadCompleted, CancellationToken cancellationToken);
    Task<ISet<OkxOptionInstrumentId>> Collect(CancellationToken cancellationToken) => Collect(null, cancellationToken);

    bool Disposed { get; }
}

public partial class OkxOptionDataCollector : IService, IOkxOptionDataCollector
{
    private readonly ILogger _logger;
    private readonly IContainer _container;
    private readonly OkxOptionDataCollectorOptions _options = new();
    private readonly OkxOptionDataWriter _writer;

    public OkxOptionDataCollector(ILogger logger, IContainer container, IConfiguration configuration)
    {
        _logger = logger.ForContext(GetType());
        _container = container;
        configuration.GetSection("Okx:Collector:Option").Bind(_options);
        _writer = new OkxOptionDataWriter(logger, configuration.GetSection("Okx:Collector:Option:Writer"), container);
    }

    public async Task<ISet<OkxOptionInstrumentId>> Collect(Action? onDownloadCompleted, CancellationToken cancellationToken)
    {
        if (!_options.Underlyings.Any())
        {
            return new HashSet<OkxOptionInstrumentId>();
        }

        await using var container = _container.GetNestedContainer();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
            container.GetInstance<Boxed<CancellationToken>>());

        cancellationToken = cts.Token;

        var http = container.GetInstance<IOkxPublicHttpApi>();
        var repo = container.GetInstance<IOkxOptionsDataRepository>();

        var res = new HashSet<OkxOptionInstrumentId>();

        await Parallel.ForEachAsync(_options.Underlyings, cancellationToken, async (underlying, cancellationToken) =>
        {
            var instrumentsTask = http.GetInstruments(OkxInstrumentType.Option, underlying: underlying,
                cancellationToken: cancellationToken);
            var tickersTask = http.GetTickers(OkxInstrumentType.Option, underlying: underlying,
                cancellationToken: cancellationToken);
            var optionsTask = http.GetOptionMarketData(underlying: underlying,
                cancellationToken: cancellationToken);
            var oiResponse = await http.GetOpenInterest(OkxInstrumentType.Option, underlying: underlying,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            OkxHttpGetInstrumentsResponse instruments;
            OkxHttpGetTickersResponse tickers;

            void UpdateRepo()
            {
                var openInterestCollection = repo.OpenInterests;
                foreach (var oi in oiResponse.data)
                {
                    openInterestCollection.AddOrUpdate(new OkxInstrumentIdentifier(oi.instId, oi.instType), _ => oi,
                        (_, prev) => oi.ts > prev.ts ? oi : prev);
                }

                var tickerCollection = repo.Tickers;
                foreach (var ticker in tickers.data)
                {
                    tickerCollection.AddOrUpdate(new OkxInstrumentIdentifier(ticker.instId, ticker.instType),
                        _ => ticker,
                        (_, prev) => ticker.ts > prev.ts ? ticker : prev);
                }
            }

            tickers = await tickersTask.ConfigureAwait(false);
            var selected = PickInstruments(oiResponse, tickers, cancellationToken);
            instruments = await instrumentsTask.ConfigureAwait(false);

            UpdateRepo();
            var instrumentDict = instruments.data.ToDictionary(info => info.instId.Value);
            var options = await optionsTask.ConfigureAwait(false);
            onDownloadCompleted?.Invoke();
            var optionDict = options.data.ToDictionary(summary => summary.instId.Value);

            var payload =
                new (OkxOptionInstrumentId, OkxHttpOpenInterest, OkxHttpTickerInfo, OkxHttpOptionSummary,
                    OkxHttpInstrumentInfo)[selected.Count];
            var c = 0;
            var minTs = long.MaxValue;
            foreach (var (optionId, (oi, ticker)) in selected)
            {
                var instId = oi.instId;
                var instrumentInfo = instrumentDict[instId];
                var optionSummary = optionDict[instId];

                payload[c] = (optionId, oi, ticker, optionSummary, instrumentInfo);
                minTs = Math.Min(minTs, oi.ts);
                minTs = Math.Min(minTs, ticker.ts);
                minTs = Math.Min(minTs, optionSummary.ts);
                bool added;
                lock (res)
                {
                    added = res.Add(optionId);
                }

                if (!added)
                {
                    _logger.Warning("duplicated inserted {Option}", optionId);
                    if (Const.IsDebug)
                    {
                        throw new Exception("duplicated option detected");
                    }
                }

                c++;
            }

            ((Span<(OkxOptionInstrumentId, OkxHttpOpenInterest, OkxHttpTickerInfo, OkxHttpOptionSummary,
                OkxHttpInstrumentInfo)>)payload).Sort(OptionDataComparison);

            Debug.Assert(minTs < long.MaxValue, "minTs < long.MaxValue");
            if (minTs < long.MaxValue)
            {
                await _writer
                    .WriteAsync(underlying, payload, DateTimeOffset.FromUnixTimeMilliseconds(minTs), cancellationToken)
                    .ConfigureAwait(false);
            }
        });

        return res;
    }

    public bool Disposed { get; private set; }

    private static readonly Xoshiro256StarStar Random = new (true);

    private Dictionary<OkxOptionInstrumentId, (OkxHttpOpenInterest, OkxHttpTickerInfo)> PickInstruments(
        OkxHttpGetOpenInterestResponse okxHttpGetOpenInterestResponse,
        OkxHttpGetTickersResponse okxHttpGetTickersResponse, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var topk =
            new TopK<(OkxHttpOpenInterest, OkxOptionInstrumentId, OkxHttpTickerInfo), OkxHttpOpenInterestComparer>(
                _options.NumberOfOptionToPick,
                new OkxHttpOpenInterestComparer(Random.NextBoolean()));

        var tickerDictionary = okxHttpGetTickersResponse.data.ToDictionary(info => info.instId);
        foreach (ref var oi in okxHttpGetOpenInterestResponse.data)
        {
            if (!OkxOptionInstrumentId.TryParse(oi.instId, out var instrumentId))
            {
                continue;
            }

            if (!tickerDictionary.TryGetValue(oi.instId, out var ticker))
            {
                continue;
            }

            topk.Add((oi, instrumentId, ticker));
        }

        return topk.ToDictionary(static tuple => tuple.Item2, static tuple => (tuple.Item1, tuple.Item3));
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int OptionDataComparison(
        (OkxOptionInstrumentId, OkxHttpOpenInterest, OkxHttpTickerInfo, OkxHttpOptionSummary, OkxHttpInstrumentInfo) x,
        (OkxOptionInstrumentId, OkxHttpOpenInterest, OkxHttpTickerInfo, OkxHttpOptionSummary, OkxHttpInstrumentInfo) y)
    {
        var cmp = OptionInstrumentIdComparison(in x.Item1, in y.Item1);

        return cmp != 0 ? cmp : x.Item2.oi.Value.CompareTo(y.Item2.oi);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
    private static int OptionInstrumentIdComparison(
        in OkxOptionInstrumentId x,
        in OkxOptionInstrumentId y)
    {
        var cmp = x.Price.CompareTo(y.Price);
        if (cmp != 0)
        {
            return cmp;
        }

        cmp = x.Year.CompareTo(y.Year);
        if (cmp != 0)
        {
            return cmp;
        }

        cmp = x.Month.CompareTo(y.Month);
        if (cmp != 0)
        {
            return cmp;
        }

        cmp = x.Day.CompareTo(y.Day);
        if (cmp != 0)
        {
            return cmp;
        }

        cmp = x.Side.CompareTo(y.Side);
        return cmp;
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync().ConfigureAwait(false);
        Dispose(false);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _writer.Dispose();
        }

        Disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}