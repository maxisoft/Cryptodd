using System.Collections.Concurrent;
using System.Collections.Immutable;
using Cryptodd.IoC;
using Cryptodd.Okx.Collectors.Options;
using Cryptodd.Okx.Http;
using Cryptodd.Okx.Models;
using Lamar;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Okx.Collectors.RubikStat;

public class RubikStatDataCollectorOptions
{
    public HashSet<string> Coins = new() { "BTC", "ETH", "XRP", "LTC" };
}

public interface IRubikStatDataCollector : IAsyncDisposable
{
    Task<IReadOnlySet<string>> Collect(Action? onDownloadCompleted, CancellationToken cancellationToken);
}

// ReSharper disable once UnusedType.Global
public class RubikStatDataCollector : IRubikStatDataCollector, IService, IDisposable
{
    private readonly IContainer _container;
    private readonly OkxRubikDataWriter _dataWriter;
    private readonly ILogger _logger;
    private readonly RubikStatDataCollectorOptions _options = new();
    private readonly ConcurrentDictionary<string, OkxRubikDataContext> _previousContexts = new();

    public RubikStatDataCollector(IContainer container, ILogger logger, IConfiguration configuration)
    {
        _container = container;
        _logger = logger.ForContext(GetType());

        configuration.GetSection("Okx:Collector:Rubik").Bind(_options);
        _dataWriter = new OkxRubikDataWriter(logger, configuration.GetSection("Okx:Collector:Rubik:Writer"), container);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public async Task<IReadOnlySet<string>> Collect(Action? onDownloadCompleted, CancellationToken cancellationToken)
    {
        await using var container = _container.GetNestedContainer();
        var api = container.GetInstance<IOkxPublicHttpRubikApi>();
        var coins = await api.GetSupportCoin(cancellationToken).ConfigureAwait(false);


        var data = new List<OkxRubikDataContext>();

        async Task CollectCoin(string coin)
        {
            var takerVolumeContractTask =
                api!.GetTakerVolume(coin, OkxInstrumentType.Contracts, cancellationToken: cancellationToken);
            var takerVolumeSpotTask =
                api.GetTakerVolume(coin, OkxInstrumentType.Spot, cancellationToken: cancellationToken);

            var marginLendingRatioTask = api.GetMarginLendingRatio(coin, cancellationToken: cancellationToken);
            var longShortRatioTask = api.GetLongShortRatio(coin, cancellationToken: cancellationToken);
            var interestAndVolumeTask =
                api.GetContractsOpenInterestAndVolume(coin, cancellationToken: cancellationToken);

            await Task.WhenAll(takerVolumeContractTask, takerVolumeSpotTask, marginLendingRatioTask, longShortRatioTask,
                interestAndVolumeTask).ConfigureAwait(false);

            var ctx = new OkxRubikDataContext(
                coin,
                takerVolumeContractTask.Result.data.MaxBy(static volume => volume.Timestamp)!,
                takerVolumeSpotTask.Result.data.MaxBy(static volume => volume.Timestamp)!,
                marginLendingRatioTask.Result.data.MaxBy(static ratio => ratio.Timestamp)!,
                longShortRatioTask.Result.data.MaxBy(static ratio => ratio.Timestamp)!,
                interestAndVolumeTask.Result.data.MaxBy(static volume => volume.Timestamp)!
            );

            if (_previousContexts.TryGetValue(coin, out var prevContext) &&
                OkxRubikDataContext.TimestampComparer.Equals(ctx, prevContext))
            {
                return;
            }

            lock (data!)
            {
                data.Add(ctx);
            }

            _previousContexts[coin] = ctx;
        }

        var tasks =
            (from coin in coins.data.contract where _options.Coins.Contains(coin) select CollectCoin(coin)).ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(false);

        onDownloadCompleted?.Invoke();

        await Parallel.ForEachAsync(data, cancellationToken,
            async (context, token) =>
            {
                var date = Math.Max(context.Item2.Timestamp, context.Item3.Timestamp);
                date = Math.Max(context.Item4.Timestamp, date);
                date = Math.Max(context.Item5.Timestamp, date);
                date = Math.Max(context.Item6.Timestamp, date);

                await _dataWriter
                    .WriteAsync(context.Item1, context, DateTimeOffset.FromUnixTimeMilliseconds(date), token)
                    .ConfigureAwait(false);
            }).ConfigureAwait(false);

        return data.Select(static context => context.Item1).ToImmutableHashSet();
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsync(true).ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dataWriter.Dispose();
        }
    }

    public virtual ValueTask DisposeAsync(bool disposing) =>
        disposing ? _dataWriter.DisposeAsync() : ValueTask.CompletedTask;
}