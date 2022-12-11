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
using GroupedOrderBookRequest = Cryptodd.Bitfinex.WebSockets.GroupedOrderBookRequest;

namespace Cryptodd.Bitfinex;

public class BitfinexGatherGroupedOrderBookService : IService
{
    protected readonly IConfiguration Configuration;
    private readonly IContainer _container;
    private readonly ILogger _logger;
    protected readonly IPairFilterLoader PairFilterLoader;
    protected int Precision { get; set; } = 2;
    protected int OrderBookLength { get; set; } = GroupedOrderBookRequest.DefaultOrderBookLength;

    private int previousNumberOfPairs = -1;

    public BitfinexGatherGroupedOrderBookService(IPairFilterLoader pairFilterLoader, ILogger logger,
        IConfiguration configuration, IContainer container)
    {
        _logger = logger.ForContext(GetType());
        PairFilterLoader = pairFilterLoader;
        Configuration = configuration;
        _container = container;
    }

    public async Task CollectOrderBooks(Action<List<OrderbookEnvelope>?> orderbookContinuation,TimeSpan downloadingTimeout = default, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        await using var container = _container.GetNestedContainer();
        var pairs = await container.GetInstance<IBitfinexPublicHttpApi>().GetAllPairs(cancellationToken)
            .ConfigureAwait(false);
        pairs = pairs.OrderBy(a => Guid.NewGuid()).ToList();
        cancellationToken.ThrowIfCancellationRequested();
        var bitfinexConfig = GetBitfinexConfig();
        var maxNumWs = bitfinexConfig.GetValue("MaxWebSockets", 10);
        var webSockets = new List<BitfinexPublicWebSocket>();
        var pairFilter = await GetPairFilterAsync(cancellationToken);
        var orderBookSection = GetOrderBookSection(bitfinexConfig);
        var tasks = new List<Task>();
        var targetBlock = new BufferBlock<OrderbookEnvelope>();
        var requests = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
        var recvDone = 0;
        var reDispatch = true;
        var processed = 0;
        var wsPool = container.GetInstance<BitfinexPublicWebSocketPool>();
        wsPool.AddSubscriber(GetType());
        maxNumWs = Math.Min(maxNumWs, wsPool.AvailableWebsocketCount);
        maxNumWs /= Math.Max(wsPool.SubscriberCount, 1);
        if (previousNumberOfPairs > 0)
        {
            maxNumWs = Math.Min((int)MathF.Log2(previousNumberOfPairs), maxNumWs);
        }
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

            using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
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
                        // prevent to schedule any remaining task
                        // and early stop
                        cancellationTokenSource.Cancel();
                        loopCancellationTokenSource.Cancel();
                        throw;
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
                        var ws = webSockets[taskNum % maxNumWs];
                        if (!ws.RegisterGroupedOrderBookRequest(pair, Precision, OrderBookLength))
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

            if (downloadingTimeout == TimeSpan.Zero)
            {
                downloadingTimeout = TimeSpan.FromSeconds(10);
            }
            cancellationTokenSource.CancelAfter(orderBookSection.GetValue("GatherTimeout", downloadingTimeout));
            
            var validators = container.GetAllInstances<IValidator<OrderbookEnvelope>>();
            while (!cancellationTokenSource.IsCancellationRequested && reDispatch)
            {
                reDispatch = DispatchTasks();
                var processTask = Parallel.ForEachAsync(webSockets, cancellationTokenSource.Token,
                    (ws, token) => ws.ProcessRequests(token));

                var orderBooks = new List<OrderbookEnvelope>();
                using var dm = new DisposableManager();

                while (processed < requests.Count && !cancellationTokenSource.IsCancellationRequested)
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
                var pingTask = Task.WhenAll(webSockets.Select(ws => ws.Ping().AsTask()));
                requests.ExceptWith(orderBooks.Select(envelope => envelope.Symbol.TrimStart('t')));
                orderbookContinuation(orderBooks);
                await DispatchOrderbookHandler(orderBooks, processed, sw, cancellationToken);
                _logger.Debug("Processed {Count} orderbooks", processed);
                await Task.WhenAll(processTask, pingTask, Task.Delay(100, cancellationToken));
                if (previousNumberOfPairs > 0)
                {
                    previousNumberOfPairs = (previousNumberOfPairs + processed + 1) / 2;
                }
                else
                {
                    previousNumberOfPairs = processed;
                }
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

    protected virtual IConfigurationSection GetOrderBookSection(IConfigurationSection bitfinexConfig) => bitfinexConfig.GetSection("OrderBook");

    protected virtual async Task<IPairFilter> GetPairFilterAsync(CancellationToken cancellationToken) => await PairFilterLoader.GetPairFilterAsync("Bitfinex/OrderBook", cancellationToken);

    protected virtual IConfigurationSection GetBitfinexConfig() => Configuration.GetSection("Bitfinex");

    private async Task DispatchOrderbookHandler(List<OrderbookEnvelope> groupedOrderBooks, int processed,
        Stopwatch stopWatch, CancellationToken cancellationToken)
    {
        var handlerTasks = new List<Task>();
        var query = new OrderbookHandlerQuery(Precision, OrderBookLength);
        var handlers = _container.GetAllInstances<IOrderbookHandler>();
        handlerTasks.AddRange(handlers.Where(handler => !handler.Disabled)
            .Select(handler => handler.Handle(query, groupedOrderBooks, cancellationToken)));

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

public class BitfinexGatherGroupedOrderBookServiceP0 : BitfinexGatherGroupedOrderBookService
{
    public BitfinexGatherGroupedOrderBookServiceP0(IPairFilterLoader pairFilterLoader, ILogger logger,
        IConfiguration configuration, IContainer container) : base(pairFilterLoader, logger, configuration, container)
    {
        Precision = 0;
        OrderBookLength = 250;
    }

    protected override async Task<IPairFilter> GetPairFilterAsync(CancellationToken cancellationToken) => await PairFilterLoader.GetPairFilterAsync("Bitfinex/OrderBookP0", cancellationToken);
    
    protected override IConfigurationSection GetOrderBookSection(IConfigurationSection bitfinexConfig) => bitfinexConfig.GetSection("OrderBookP0");
}