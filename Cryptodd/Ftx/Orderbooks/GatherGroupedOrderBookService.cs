using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using Cryptodd.Ftx.Models;
using Cryptodd.Ftx.Orderbooks.RegroupedOrderbooks;
using Cryptodd.IoC;
using Cryptodd.Pairs;
using Cryptodd.Utils;
using Lamar;
using Maxisoft.Utils.Disposables;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;

namespace Cryptodd.Ftx.Orderbooks;

public sealed class GatherGroupedOrderBookService : IService
{
    private readonly IConfiguration _configuration;
    private readonly IContainer _container;
    private readonly ILogger _logger;
    private readonly IPairFilterLoader _pairFilterLoader;

    public GatherGroupedOrderBookService(IPairFilterLoader pairFilterLoader, ILogger logger,
        IConfiguration configuration, IContainer container)
    {
        _logger = logger.ForContext<GatherGroupedOrderBookService>();
        _pairFilterLoader = pairFilterLoader;
        _configuration = configuration;
        _container = container;
    }

    public async Task CollectOrderBooks(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        await using var container = _container.GetNestedContainer();
        using var api = container.GetInstance<IFtxPublicHttpApi>();
        using var markets = await api.GetAllMarketsAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var ftxConfig = _configuration.GetSection("Ftx");
        var maxNumWs = ftxConfig.GetValue("MaxWebSockets", 10);
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
                var ws = container.GetInstance<FtxGroupedOrderBookWebsocket>();
                webSockets.Add(ws);
                ws.RegisterTargetBlock(targetBlock);
                Debug.Assert(i == 0 || !ReferenceEquals(webSockets[^1], webSockets[^2]));
            }

            var taskNum = 0;

            void DispatchTasks()
            {
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
            }

            DispatchTasks();
            

            using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cancellationTokenSource.CancelAfter(groupedOrderBookSection.GetValue("GatherTimeout", 60 * 1000));
            await Parallel.ForEachAsync(webSockets, cancellationTokenSource.Token,
                (ws, token) => ws.ProcessRequests(token));

            var processed = 0;
            var groupedOrderBooks = new List<GroupedOrderbookDetails>();
            using var dm = new DisposableManager();
            var validators = container.GetAllInstances<IValidator<GroupedOrderbookDetails>>();
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
                    _logger.Debug("Orderbook for {Market} has invalid field(s): {Fields}", resp.Market, string.Join(" ", details.InvalidFields.Keys));
                }

                if (!valid)
                {
                    continue;
                }

                groupedOrderBooks.Add(resp);
                processed += 1;

            }


            var dispatchObHandler = DispatchOrderbookHandler(groupedOrderBooks, processed, sw, cancellationToken);
            var dispatchGroupedObHandler = DispatchRegroupedOrderbookHandler(groupedOrderBooks, sw, container, cancellationToken);
            await Task.WhenAll(dispatchObHandler, dispatchGroupedObHandler).ConfigureAwait(false);
        }
        finally
        {
            await Parallel.ForEachAsync(webSockets, cancellationToken, (ws, token) => ws.DisposeAsync())
                .ConfigureAwait(false);
            targetBlock.Complete();
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task DispatchRegroupedOrderbookHandler(List<GroupedOrderbookDetails> groupedOrderBooks,
        Stopwatch stopWatch, IServiceContext container, CancellationToken cancellationToken = default)
    {
        var regroupedHandlers = container.GetAllInstances<IRegroupedOrderbookHandler>();
        ConcurrentBag<RegroupedOrderbook> regroupedOrderbooks;
        if (regroupedHandlers.Any() && groupedOrderBooks.Any())
        {
            regroupedOrderbooks = new ConcurrentBag<RegroupedOrderbook>();
            Parallel.ForEach(groupedOrderBooks, details =>
            {
                try
                {
                    var regroupedOrderbook = RegroupedOrderbookAlgorithm.Create(details);
                    regroupedOrderbooks.Add(regroupedOrderbook);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Error while creating regrouped orderbook for {Market} #asks:{Asks} #bids{Bids}",
                        details.Market, details.Data.Asks.Count, details.Data.Bids.Count);
                }
            });
        }
        else
        {
            _logger.Debug("There's no need to compute regrouped orderbook as there's no handler");
            return;
        }

        var handlerTasks = new List<Task>();
        handlerTasks.AddRange(regroupedHandlers.Where(handler => !handler.Disabled)
            .Select(handler => handler.Handle(regroupedOrderbooks, cancellationToken)));

        var errorCount = 0;
        try
        {
            await Task.WhenAll(handlerTasks).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            errorCount += 1;
            _logger.Error(e, "Unable to handle {Count} groupedorderbooks", regroupedOrderbooks.Count);
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
                _logger.Error(e, "A regrouped orderbook handler threw up. This shouldn't occurs !");
            }
        }

        if (errorCount == 0)
        {
            _logger.Verbose("Processed {Count} regrouped orderbooks in {Elapsed}", regroupedOrderbooks.Count,
                stopWatch.Elapsed);
        }
        else
        {
            _logger.Warning("Got {ErrorCount} errors while processing {Count} regrouped orderbooks in {Elapsed}",
                errorCount,
                regroupedOrderbooks.Count,
                stopWatch.Elapsed);
        }
    }

    private async Task DispatchOrderbookHandler(List<GroupedOrderbookDetails> groupedOrderBooks, int processed,
        Stopwatch stopWatch, CancellationToken cancellationToken)
    {
        var handlerTasks = new List<Task>();
        var handlers = _container.GetAllInstances<IGroupedOrderbookHandler>();
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


    private static double ComputeGrouping(Market market, double percent, double markPrice)
    {
        var grouping = percent / 100.0 * markPrice;
        grouping = PriceUtils.Round(grouping, market.PriceIncrement);
        grouping = Math.Max(grouping, market.PriceIncrement);
        return grouping;
    }
}