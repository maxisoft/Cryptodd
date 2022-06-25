using System.Collections.Concurrent;
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
        _logger = logger.ForContext<BitfinexGatherGroupedOrderBookService>();
        _pairFilterLoader = pairFilterLoader;
        _configuration = configuration;
        _container = container;
    }

    public async Task CollectOrderBooks(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        await using var container = _container.GetNestedContainer();
        var pairs = await container.GetInstance<IBitfinexPublicHttpApi>().GetAllPairs(cancellationToken).ConfigureAwait(false);
        pairs = pairs.OrderBy(a => Guid.NewGuid()).ToList();
        cancellationToken.ThrowIfCancellationRequested();
        var bitfinexConfig = _configuration.GetSection("Bitfinex");
        var maxNumWs = bitfinexConfig.GetValue("MaxWebSockets", 7);
        var webSockets = new List<BitfinexPublicWs>();
        var pairFilter = await _pairFilterLoader.GetPairFilterAsync("Bitfinex/OrderBook", cancellationToken);
        var orderBookSection = bitfinexConfig.GetSection("OrderBook");
        var tasks = new List<Task>();
        var targetBlock = new BufferBlock<OrderbookEnvelope>();
        var requests = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
        var recvDone = 0;
        var reDispatch = true;
        var processed = 0;
        try
        {
            for (var i = 0; i < maxNumWs; i++)
            {
                var ws = container.GetInstance<BitfinexPublicWs>();
                webSockets.Add(ws);
                ws.RegisterTargetBlock(targetBlock);
                Debug.Assert(i == 0 || !ReferenceEquals(webSockets[^1], webSockets[^2]));
            }

            var taskNum = 0;

            bool DispatchTasks()
            {
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
                            tasks.Add(webSockets[taskNum % maxNumWs].RecvLoop()
                                .ContinueWith(recvTask =>
                                {
                                    Interlocked.Increment(ref recvDone);
                                    if (recvTask.IsFaulted)
                                    {
                                        _logger.Error(recvTask.Exception, "");
                                    }
                                }, cancellationToken));
                        }

                        taskNum++;
                    }
                }

                return res;
            }

            using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cancellationTokenSource.CancelAfter(orderBookSection.GetValue("GatherTimeout", 20 * 1000));
            
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
                        if (details is null || !_logger.IsEnabled(LogEventLevel.Debug)) continue;
                        _logger.Debug("Orderbook for {Symbol} has invalid field(s): {Fields}", resp.Symbol, string.Join(" ", details.InvalidFields.Keys));
                    }

                    if (!valid)
                    {
                        continue;
                    }

                    orderBooks.Add(resp);
                    processed += 1;
                }

                requests.ExceptWith(orderBooks.Select(envelope => envelope.Symbol.TrimStart('t')));
                await DispatchOrderbookHandler(orderBooks, processed, sw, cancellationToken);
            }
        }
        finally
        {
            await Parallel.ForEachAsync(webSockets, cancellationToken, (ws, token) => ws.DisposeAsync())
                .ConfigureAwait(false);
            targetBlock.Complete();
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
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
                task.Wait(cancellationToken);
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