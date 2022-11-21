using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using Cryptodd.Bitfinex.Models;
using Cryptodd.Bitfinex.Orderbooks;
using Cryptodd.Bitfinex.WebSockets;
using Cryptodd.Ftx.Orderbooks;
using Cryptodd.IoC;
using Cryptodd.Pairs;
using Lamar;
using Maxisoft.Utils.Disposables;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;

namespace Cryptodd.Bitfinex;

public class BitfinexGatherGroupedOrderBookService : IService
{
    private readonly IConfiguration _configuration;
    private readonly IContainer _container;
    private readonly ILogger _logger;
    private readonly IPairFilterLoader _pairFilterLoader;

    public BitfinexGatherGroupedOrderBookService(IPairFilterLoader pairFilterLoader, ILogger logger,
        IConfiguration configuration, IContainer container)
    {
        _logger = logger.ForContext(GetType());
        _pairFilterLoader = pairFilterLoader;
        _configuration = configuration;
        _container = container;
    }

    public async Task CollectOrderBooks(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        await using var container = _container.GetNestedContainer();
        var pairs = await container.GetInstance<IBitfinexPublicHttpApi>().GetAllPairs(cancellationToken)
            .ConfigureAwait(false);
        pairs = pairs.OrderBy(a => Guid.NewGuid()).ToList();
        cancellationToken.ThrowIfCancellationRequested();
        var bitfinexConfig = _configuration.GetSection("Bitfinex");
        var maxNumWs = bitfinexConfig.GetValue("MaxWebSockets", 30);
        var webSockets = new List<BitfinexPublicWebSocket>();
        var pairFilter = await _pairFilterLoader.GetPairFilterAsync("Bitfinex/OrderBook", cancellationToken);
        var orderBookSection = bitfinexConfig.GetSection("OrderBook");
        var tasks = new List<Task>();
        var targetBlock = new BufferBlock<OrderbookEnvelope>();
        var requests = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
        var recvDone = 0;
        var reDispatch = true;
        var processed = 0;
        var wsPool = container.GetInstance<BitfinexPublicWebSocketPool>();
        maxNumWs = Math.Min(maxNumWs, wsPool.AvailableWebsocketCount);
        maxNumWs = Math.Max(maxNumWs, 1);
        using var loopCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            for (var i = 0; i < maxNumWs; i++)
            {
                var ws = await wsPool.GetWebSocket(container, cancellationToken);
                webSockets.Add(ws);
                ws.RegisterTargetBlock(targetBlock);
                Debug.Assert(i == 0 || !ReferenceEquals(webSockets[^1], webSockets[^2]));
            }

            var taskNum = 0;


            bool DispatchTasks()
            {
                async Task RecvLoop(int i)
                {
                    var ws = webSockets[i];
                    try
                    {
                        await ws.RecvLoop(loopCancellationTokenSource.Token).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, "{Method} task {Identifier} failed", nameof(RecvLoop), i);
                        ws.Close();
                    }
                    finally
                    {
                        Interlocked.Increment(ref recvDone);
                        await ws.DisposeAsync();
                    }
                }

                var res = false;
                foreach (var pair in pairs)
                {
                    if (pairFilter.Match(pair) && !requests.Contains(pair))
                    {
                        if (!webSockets[taskNum % maxNumWs].RegisterGroupedOrderBookRequest(pair, 2))
                        {
                            res = true;
                            continue;
                        }

                        requests.Add(pair);
                        if (taskNum < webSockets.Count)
                        {
                            var task = RecvLoop(taskNum);
                            tasks.Add(task);
                        }

                        taskNum++;
                    }
                }

                return res;
            }

            using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cancellationTokenSource.CancelAfter(orderBookSection.GetValue("GatherTimeout", 10 * 1000));

            while (!cancellationTokenSource.IsCancellationRequested && reDispatch)
            {
                reDispatch = DispatchTasks();
                await Parallel.ForEachAsync(webSockets, cancellationTokenSource.Token,
                    (ws, token) => ws.ProcessRequests(token));

                var orderBooks = new List<OrderbookEnvelope>();
                using var dm = new DisposableManager();
                var validators = container.GetAllInstances<IValidator<OrderbookEnvelope>>();
                while (processed < requests.Count && !cancellationToken.IsCancellationRequested)
                {
                    if (recvDone >= tasks.Count)
                    {
                        _logger.Warning("Sockets got disconnected");
                        break;
                    }

                    OrderbookEnvelope resp;

                    try
                    {
                        resp = await targetBlock.ReceiveAsync(cancellationTokenSource.Token).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is OperationCanceledException or TaskCanceledException)
                    {
                        break;
                    }

                    dm.LinkDisposable(resp);

                    var valid = true;
                    foreach (var validator in validators)
                    {
                        if (validator.Validate(resp, out var details))
                        {
                            continue;
                        }

                        valid = false;
                        if (details is null || !_logger.IsEnabled(LogEventLevel.Debug))
                        {
                            continue;
                        }

                        _logger.Debug("Orderbook for {Symbol} has invalid field(s): {Fields}", resp.Symbol,
                            string.Join(" ", details.InvalidFields.Keys));
                    }

                    if (!valid)
                    {
                        continue;
                    }

                    orderBooks.Add(resp);
                    processed += 1;
                }
                
                cancellationTokenSource.Cancel();
                loopCancellationTokenSource.Cancel();
                // send a ping to recv a pong message and stop RecvLoop
                await Task.WhenAll(webSockets.Select(ws => ws.Ping().AsTask()));
                requests.ExceptWith(orderBooks.Select(envelope => envelope.Symbol.TrimStart('t')));
                await DispatchOrderbookHandler(orderBooks, processed, sw, cancellationToken);
                _logger.Debug("Processed {Count} orderbooks", processed);
                await Task.Delay(100, cancellationToken);
            }
        }
        finally
        {
            foreach (var task in tasks)
            {
                if (task.IsCompleted || task.IsCanceled)
                {
                    task.Dispose();
                }
                
            }
            tasks.Clear();
            await Parallel.ForEachAsync(webSockets, cancellationToken, async (ws, token) =>
                {
                    if (!token.IsCancellationRequested)
                    {
                        await ws.DisposeAsync().ConfigureAwait(false);
                    }

                    ws.Close();
                    // ReSharper disable once MethodHasAsyncOverload
                    ws.Dispose();
                })
                .ConfigureAwait(false);
            targetBlock.Complete();
        }
    }

    private async Task DispatchOrderbookHandler(List<OrderbookEnvelope> groupedOrderBooks, int processed,
        Stopwatch stopWatch, CancellationToken cancellationToken)
    {
        var handlerTasks = new List<Task>();
        var handlers = _container.GetAllInstances<IOrderbookHandler>();
        handlerTasks.AddRange(handlers.Where(handler => !handler.Disabled)
            .Select(handler => handler.Handle(groupedOrderBooks, cancellationToken)));

        var errorCount = 0;
        try
        {
            await Task.WhenAll(handlerTasks).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            errorCount += 1;
            _logger.Error(e, "Unable to handle {Count} orderbooks", groupedOrderBooks.Count);
        }

        foreach (var task in handlerTasks)
        {
            try
            {
                // ReSharper disable once MethodHasAsyncOverload
                await task.WaitAsync(cancellationToken);
            }
            catch (Exception e)
            {
                errorCount += 1;
                _logger.Error(e, "An orderbook handler throwed up. This shouldn't occurs !");
            }
        }

        if (errorCount == 0)
        {
            _logger.Verbose("Processed {Count} grouped orderbooks in {Elapsed}", processed, stopWatch.Elapsed);
        }
        else
        {
            _logger.Warning("Got {ErrorCount} errors while processing {Count} grouped orderbooks in {Elapsed}",
                errorCount, processed,
                stopWatch.Elapsed);
        }
    }
}