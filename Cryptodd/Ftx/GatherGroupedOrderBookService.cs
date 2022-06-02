using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using Cryptodd.Ftx.Models;
using Cryptodd.IoC;
using Cryptodd.Pairs;
using Cryptodd.Utils;
using Lamar;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Ftx;

public class GatherGroupedOrderBookService : IService, IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly IContainer _container;
    private readonly ILogger _logger;
    private readonly IPairFilterLoader _pairFilterLoader;

    public GatherGroupedOrderBookService(IPairFilterLoader pairFilterLoader, ILogger logger,
        IConfiguration configuration, IContainer container)
    {
        _logger = logger;
        _pairFilterLoader = pairFilterLoader;
        _configuration = configuration;
        _container = container;
    }

    private FtxPublicHttpApi FtxPublicHttpApi => _container.GetInstance<FtxPublicHttpApi>();

    public void Dispose() { }

    public async Task CollectOrderBooks(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var markets = await FtxPublicHttpApi.GetAllMarketsAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var ftxConfig = _configuration.GetSection("Ftx");
        var maxNumWs = ftxConfig.GetValue("MaxWebSockets", 20);
        var webSockets = new List<FtxGroupedOrderBookWebsocket>();
        var pairFilter = await _pairFilterLoader.GetPairFilterAsync("Ftx/GroupedOrderBook", cancellationToken);
        var groupedOrderBookSection = ftxConfig.GetSection("GroupedOrderBook");
        var percent = groupedOrderBookSection.GetValue("Percent", 0.05);
        var tasks = new List<Task>();
        var targetBlock = new BufferBlock<GroupedOrderbookDetails>();
        var requests = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
        var recvDone = 0;
        try
        {
            for (var i = 0; i < maxNumWs; i++)
            {
                var ws = _container.GetInstance<FtxGroupedOrderBookWebsocket>();
                webSockets.Add(ws);
                ws.RegisterTargetBlock(targetBlock);
                Debug.Assert(i == 0 || !ReferenceEquals(webSockets[^1], webSockets[^2]));
            }

            var taskNum = 0;

            foreach (var market in markets)
            {
                if (market.Enabled && !market.PostOnly && market.Ask is > 0 && market.Bid is > 0 &&
                    pairFilter.Match(market.Name) && !requests.Contains(market.Name))
                {
                    var markPrice = 0.5 * (market.Ask.Value + market.Bid.Value);
                    var grouping = ComputeGrouping(market, percent, markPrice);
                    Debug.Assert(grouping > 0);
                    if (!webSockets[taskNum % maxNumWs].RegisterGroupedOrderBookRequest(market.Name, grouping))
                    {
                        continue;
                    }

                    requests.Add(market.Name);
                    if (taskNum < webSockets.Count)
                    {
                        tasks.Add(webSockets[taskNum % maxNumWs].RecvLoop()
                            .ContinueWith(_ => { Interlocked.Increment(ref recvDone); }, cancellationToken));
                    }

                    taskNum++;
                }
            }

            using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cancellationTokenSource.CancelAfter(groupedOrderBookSection.GetValue("GatherTimeout", 60 * 1000));
            await Parallel.ForEachAsync(webSockets, cancellationTokenSource.Token,
                (ws, token) => ws.ProcessRequests(token));

            var processed = 0;
            var handlers = _container.GetAllInstances<IGroupedOrderbookHandler>();
            var groupedOrderBooks = new List<GroupedOrderbookDetails>();
            while (processed < requests.Count && !cancellationToken.IsCancellationRequested)
            {
                if (recvDone >= tasks.Count)
                {
                    _logger.Warning("Sockets got disconnected");
                    break;
                }

                GroupedOrderbookDetails resp;

                try
                {
                    resp = await targetBlock.ReceiveAsync(cancellationTokenSource.Token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is OperationCanceledException or TaskCanceledException)
                {
                    break;
                }

                groupedOrderBooks.Add(resp);
                processed += 1;
            }

            var handlerTasks = new List<Task>();
            handlerTasks.AddRange(handlers.Where(handler => !handler.Disabled)
                .Select(handler => handler.Handle(groupedOrderBooks, cancellationToken)));
            await Task.WhenAll(handlerTasks).ConfigureAwait(false);
            _logger.Information("Processed {Count} grouped orderbooks in {Elapsed}", processed, sw.Elapsed);
        }
        finally
        {
            await Parallel.ForEachAsync(webSockets, cancellationToken, (ws, token) => ws.DisposeAsync())
                .ConfigureAwait(false);
            targetBlock.Complete();
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }


    private static double ComputeGrouping(Market market, double percent, double markPrice)
    {
        var grouping = percent / 100.0 * markPrice;
        grouping = PriceUtils.Round(grouping, market.PriceIncrement);
        grouping = Math.Max(grouping, market.PriceIncrement);
        return grouping;
    }
}