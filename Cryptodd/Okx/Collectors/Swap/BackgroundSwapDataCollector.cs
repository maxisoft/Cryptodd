using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using Cryptodd.IoC;
using Cryptodd.Okx.Http;
using Cryptodd.Okx.Models;
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

                swapDataRepository.FundingRates.AddOrUpdate(identifier, _ => fr,
                    (_, prev) => fr.fundingTime >= prev.fundingTime ? fr : prev);
            }
        }

        async Task PeriodicOpenInterest()
        {
            var sw = Stopwatch.StartNew();
            while (!cancellationToken.IsCancellationRequested)
            {
                sw.Restart();
                var openInterests =
                    await http.GetOpenInterest(OkxInstrumentType.Swap, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                static int ProcessOpenInterests(OkxHttpGetOpenInterestResponse response,
                    ISwapDataRepository swapDataRepository)
                {
                    var c = 0;
                    foreach (var openInterest in response.data)
                    {
                        var identifier =
                            new OkxInstrumentIdentifier(openInterest.instId, openInterest.instType);
                        swapDataRepository.OpenInterests.AddOrUpdate(identifier, _ => openInterest,
                            (_, prev) => openInterest.ts >= prev.ts ? openInterest : prev);
                        c++;
                    }

                    return c;
                }

                ProcessOpenInterests(openInterests, swapDataRepository);

                var delay = Math.Max(1000 - sw.ElapsedMilliseconds, 0);
                await Task.Delay(checked((int)delay), cancellationToken).ConfigureAwait(false);
            }
        }

        var bufferTask = ProcessWebsocketBuffer();
        var periodicTask = PeriodicOpenInterest();

        await subscriptionTask.ConfigureAwait(false);

        await Task.WhenAll(receiveLoop, activityTask, bufferTask, periodicTask).ConfigureAwait(false);
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