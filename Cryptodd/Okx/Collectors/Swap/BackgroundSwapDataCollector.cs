using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using Cryptodd.IoC;
using Cryptodd.Json;
using Cryptodd.Okx.Http;
using Cryptodd.Okx.Models;
using Cryptodd.Okx.Models.HttpResponse;
using Cryptodd.Okx.Websockets;
using Cryptodd.Okx.Websockets.Subscriptions;
using Cryptodd.Pairs;
using Lamar;
using Serilog;

namespace Cryptodd.Okx.Collectors.Swap;

// ReSharper disable once ClassNeverInstantiated.Global
public class BackgroundSwapDataCollector : IService, IBackgroundSwapDataCollector
{
    private readonly IContainer _container;
    private readonly ILogger _logger;

    public BackgroundSwapDataCollector(IContainer container, ILogger logger)
    {
        _container = container;
        _logger = logger.ForContext(GetType());
    }

    public async Task CollectLoop(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellationToken = cts.Token;
        await using var container = _container.GetNestedContainer();
        var swapDataRepository = container.GetInstance<ISwapDataRepository>();
        var http = container.GetInstance<IOkxPublicHttpApi>();
        await using var ws = container.GetInstance<OkxWebsocketForFundingRate>();
        await ws.ConnectIfNeeded(cancellationToken).ConfigureAwait(false);
        var receiveLoop = ws.ReceiveLoop(cancellationToken);
        var activityTask = ws.EnsureConnectionActivityTask(cancellationToken);

        var bufferBlock = new BufferBlock<OkxWebsocketFundingRateResponse>(new DataflowBlockOptions
            { CancellationToken = cancellationToken, BoundedCapacity = 8 << 10 });
        ws.AddBufferBlock(bufferBlock);

        var subscriptionTask = PerformSubscriptions(ws, http, container, cancellationToken);

        async Task ProcessWebsocketBuffer()
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var fundingRateResponse = await bufferBlock.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                if (!fundingRateResponse.data.HasValue)
                {
                    continue;
                }

                var fr = fundingRateResponse.data.Value;
                var identifier = new OkxInstrumentIdentifier(fr.instId, fr.instType);

                swapDataRepository.FundingRates.AddOrUpdate(identifier,
                    _ => fr,
                    (_, prev) => fr.ts > prev.ts && fr.fundingTime >= prev.fundingTime
                        ? fr
                        : prev);
            }
        }

        async Task PeriodicOpenInterestAndTickersAndMarkPrices()
        {
            var sw = Stopwatch.StartNew();
            var instrumentSw = new Stopwatch();
            Dictionary<OkxInstrumentIdentifier, OkxHttpInstrumentInfo> instruments = new();
            while (!cancellationToken.IsCancellationRequested)
            {
                sw.Restart();

                var openInterestsTask =
                    http.GetOpenInterest(OkxInstrumentType.Swap, cancellationToken: cancellationToken);

                var tickersTask = http.GetTickers(OkxInstrumentType.Swap, cancellationToken: cancellationToken);

                var markPricesTask = http.GetMarkPrices(OkxInstrumentType.Swap, cancellationToken: cancellationToken);

                if (!instrumentSw.IsRunning || instrumentSw.ElapsedMilliseconds > 30_000)
                {
                    var rawInstruments = await
                        http.GetInstruments(OkxInstrumentType.Swap, cancellationToken: cancellationToken)
                            .ConfigureAwait(false);
                    instruments =
                        rawInstruments.data.ToDictionary(
                            info => new OkxInstrumentIdentifier(info.instId, info.instType));
                    instrumentSw.Restart();
                }

                var openInterests = await openInterestsTask.ConfigureAwait(false);
                var rawTickers = await tickersTask.ConfigureAwait(false);
                var rawMarkPrices = await markPricesTask.ConfigureAwait(false);
                var tickers = new Lazy<Dictionary<OkxInstrumentIdentifier, OkxHttpTickerInfo>>(() =>
                        rawTickers.data.ToDictionary(info => new OkxInstrumentIdentifier(info.instId, info.instType)))
                    ;

                var markPrices = new Lazy<Dictionary<OkxInstrumentIdentifier, OkxHttpMarkPrice>>(() =>
                        rawMarkPrices.data.ToDictionary(info =>
                            new OkxInstrumentIdentifier(info.instId, info.instType)))
                    ;

                int ProcessOpenInterests(OkxHttpGetOpenInterestResponse response)
                {
                    var c = 0;
                    var openInterestCollection = swapDataRepository.OpenInterests;
                    foreach (var openInterest in response.data)
                    {
                        var identifier =
                            new OkxInstrumentIdentifier(openInterest.instId, openInterest.instType);
                        var mult = 1.0;
                        if (instruments.TryGetValue(identifier, out var instrumentInfo))
                        {
                            mult = instrumentInfo.ctVal;
                            if (mult <= 0)
                            {
                                _logger.Warning("{Symbol} has invalid ctVal {CtVal}", identifier.Id, mult);
                                mult = 1.0;
                            }
                        }

                        var targetOi = openInterest;
                        // ReSharper disable once CompareOfFloatsByEqualityOperator
                        if (mult != 1.0)
                        {
                            targetOi = openInterest with { oi = openInterest.oi * mult };
                        }

                        /* fix "no meaning" open interest to suit the following equation : 
                         * oi / oiCcy = current price
                         */
                        if (openInterest.oiCcy > 0 && targetOi.oi / openInterest.oiCcy - 1 < 0.001)
                        {
                            double price = 0;
                            if (markPrices.Value.TryGetValue(identifier, out var markPrice) && markPrice.markPx > 0)
                            {
                                price = markPrice.markPx;
                            }
                            else if (tickers.Value.TryGetValue(identifier, out var tickerInfo))
                            {
                                price = tickerInfo.last;
                            }

                            if (price > 0)
                            {
                                targetOi = targetOi with { oi = openInterest.oiCcy * price };
                            }
                        }

                        openInterestCollection.AddOrUpdate(identifier, _ => targetOi,
                            (_, prev) => openInterest.ts >= prev.ts ? targetOi : prev);
                        c++;
                    }

                    return c;
                }

                ProcessOpenInterests(openInterests);

                int ProcessTickers(OkxHttpGetTickersResponse response)
                {
                    var c = 0;
                    var tickerCollection = swapDataRepository.Tickers;
                    foreach (var info in response.data)
                    {
                        var identifier =
                            new OkxInstrumentIdentifier(info.instId, info.instType);
                        tickerCollection.AddOrUpdate(identifier, _ => info,
                            (_, prev) => info.ts >= prev.ts ? info : prev);
                        c++;
                    }

                    return c;
                }

                ProcessTickers(rawTickers);


                int ProcessMarkPrices(OkxHttpGetMarkPriceResponse response)
                {
                    var c = 0;
                    var pricesCollection = swapDataRepository.MarkPrices;
                    foreach (var info in response.data)
                    {
                        var identifier =
                            new OkxInstrumentIdentifier(info.instId, info.instType);
                        pricesCollection.AddOrUpdate(identifier, _ => info,
                            (_, prev) => info.ts >= prev.ts ? info : prev);
                        c++;
                    }

                    return c;
                }

                ProcessMarkPrices(rawMarkPrices);

                var delay = Math.Max(1000 - sw.ElapsedMilliseconds, 0);
                await Task.Delay(checked((int)delay), cancellationToken).ConfigureAwait(false);
            }
        }

        var bufferTask = ProcessWebsocketBuffer();
        var periodicTask = PeriodicOpenInterestAndTickersAndMarkPrices();

        await subscriptionTask.ConfigureAwait(false);

        try
        {
            await Task.WhenAny(receiveLoop, activityTask, bufferTask, periodicTask).ConfigureAwait(false);
        }
        finally
        {
            await cts.CancelAsync();
        }
    }

    private static async Task PerformSubscriptions(OkxWebsocketForFundingRate ws, IOkxInstrumentIdsProvider http,
        IServiceContext container, CancellationToken cancellationToken)
    {
        var connectTask = ws.ConnectIfNeeded(cancellationToken).AsTask();
        var loadPairFilterTask = container.GetInstance<IPairFilterLoader>()
            .GetPairFilterAsync("Okx:SwapDataCollector", cancellationToken);
        var swaps = await http.ListInstrumentIds(OkxInstrumentType.Swap, cancellationToken: cancellationToken);

        var pairFilter = await loadPairFilterTask.ConfigureAwait(false);

        var filtered = swaps.Where(s => pairFilter.Match(s)).Select(static s => new OkxFundingRateSubscription(s))
            .ToList();
        await connectTask.ConfigureAwait(false);
        await ws.MultiSubscribe(filtered, cancellationToken).ConfigureAwait(false);
    }
}