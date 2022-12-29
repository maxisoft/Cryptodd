using System.Collections;
using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using Cryptodd.IoC;
using Cryptodd.Okx.Models;
using Cryptodd.Okx.Orderbooks.Handlers;
using Cryptodd.Okx.Websockets;
using Cryptodd.Okx.Websockets.Pool;
using Cryptodd.Okx.Websockets.Subscriptions;
using Cryptodd.Pairs;
using Lamar;
using Maxisoft.Utils.Collections.Lists;
using Maxisoft.Utils.Disposables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Cryptodd.Okx.Orderbooks;

public interface IOkxOrderbookCollector
{
    Task<int> CollectOrderbooks(Action? orderbookContinuation = null,
        TimeSpan downloadingTimeout = default, CancellationToken cancellationToken = default);
}

public class OkxOrderbookCollector : IOkxOrderbookCollector, IService
{
    private readonly IContainer _container;
    private readonly IOkxOrderbookInstrumentLister _instrumentLister;
    private readonly IPairFilterLoader _pairFilterLoader;
    private readonly ILogger _logger;
    private readonly OkxOrderbookCollectorOptions _options = new();


    public OkxOrderbookCollector(IContainer container, ILogger logger, IOkxOrderbookInstrumentLister instrumentLister,
        IConfiguration configuration, IPairFilterLoader pairFilterLoader)
    {
        _container = container;
        _logger = logger.ForContext(GetType());
        _instrumentLister = instrumentLister;
        _pairFilterLoader = pairFilterLoader;
        configuration.GetSection("Okx:Orderbook:Collector").Bind(_options);
    }

    private int _previousInstrumentCount;

    private async ValueTask<List<string>> ListInstruments(IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        using var instrumentsTask = _instrumentLister.ListInstruments(cancellationToken);
        var pairFilter = await _pairFilterLoader.GetPairFilterAsync("Okx:Orderbook", cancellationToken)
            .ConfigureAwait(false);
        var instruments = await instrumentsTask.ConfigureAwait(false);
        var res = new List<string>(_previousInstrumentCount);
        res.AddRange(instruments.Where(instrument => pairFilter.Match(instrument)));
        _previousInstrumentCount = res.Capacity;
        return res;
    }

    private async Task<int> ProcessBufferBlock<TBlock>(int targetCounter, IServiceContext container, TBlock bufferBlock,
        CancellationToken cancellationToken)
        where TBlock : ISourceBlock<OkxWebsocketOrderbookResponse>,
        IReceivableSourceBlock<OkxWebsocketOrderbookResponse>
    {
        var completed = 0;
        var handlers = container.GetAllInstances<IOkxOrderbookHandler>();
        var groupedHandlers = container.GetAllInstances<IOkxGroupedOrderbookHandler>();
        var aggregator = container.GetService<IOkxOrderbookAggregator>();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var prevToken = cancellationToken;
        cancellationToken = cts.Token;

        static Task WhenAll(ArrayList<Task> tasks)
        {
            if (tasks.Count == 0)
            {
                return Task.CompletedTask;
            }

            return tasks.Capacity == tasks.Count ? Task.WhenAll(tasks.Data()) : Task.WhenAll(tasks);
        }

        async Task<int> Dispatch<TList>(TList responses, CancellationToken cancellationToken)
            where TList : IList<OkxWebsocketOrderbookResponse>
        {
            try
            {
                ArrayList<Task> handlerTasks;

                if (handlers is { Count: > 0 })
                {
                    handlerTasks = new ArrayList<Task>(handlers.Count);
                    foreach (var handler in handlers)
                    {
                        handlerTasks.Add(handler.Handle(responses, cancellationToken));
                    }
                }
                else
                {
                    handlerTasks = new ArrayList<Task>();
                }

                ArrayList<Task> groupedHandlerTasks;
                if (groupedHandlers is { Count: > 0 } && aggregator is not null)
                {
                    groupedHandlerTasks = new ArrayList<Task>(groupedHandlers.Count);
                    var aggregatedTasks = new ArrayList<Task<OkxAggregatedOrderbookHandlerArguments>>(responses.Count);
                    foreach (var response in responses)
                    {
                        aggregatedTasks.Add(aggregator.Handle(response, cancellationToken).AsTask());
                    }

                    Debug.Assert(aggregatedTasks.Capacity == aggregatedTasks.Count);
                    while (aggregatedTasks.Count < aggregatedTasks.Capacity)
                    {
                        aggregatedTasks.Add(Task.FromResult<OkxAggregatedOrderbookHandlerArguments>(null));
                    }

                    await Task.WhenAll(aggregatedTasks.Data());

                    for (var i = 0; i < aggregatedTasks.Count; i++)
                    {
                        var argument = await aggregatedTasks[i].ConfigureAwait(false);
                        foreach (var handler in groupedHandlers)
                        {
                            groupedHandlerTasks.Add(handler.Handle(argument, cancellationToken));
                        }
                    }
                }
                else
                {
                    groupedHandlerTasks = new ArrayList<Task>();
                }

                await WhenAll(handlerTasks).ConfigureAwait(false);
                await WhenAll(groupedHandlerTasks).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Add(ref completed, responses.Count);
                if (completed >= targetCounter)
                {
                    try
                    {
                        // ReSharper disable once AccessToDisposedClosure
                        cts.Cancel();
                    }
                    catch (ObjectDisposedException e)
                    {
                        _logger.Debug(e, "cts is dipsosed");
                    }
                }

                foreach (var response in responses)
                {
                    response.Dispose();
                }
            }


            return responses.Count;
        }

        var dispatchTasks = new List<Task<int>>(_previousInstrumentCount);

        while (!prevToken.IsCancellationRequested && completed < targetCounter)
        {
            OkxWebsocketOrderbookResponse ob;
            try
            {
                ob = await bufferBlock.ReceiveAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (!cts.IsCancellationRequested || prevToken.IsCancellationRequested)
                {
                    throw;
                }

                break;
            }


            if (bufferBlock.TryReceiveAll(out var obs))
            {
                obs.Add(ob);
                dispatchTasks.Add(Dispatch(obs, prevToken));
            }
            else
            {
                var obArray = new[] { ob };
                dispatchTasks.Add(Dispatch(obArray, prevToken));
            }
        }

        await Task.WhenAll(dispatchTasks).ConfigureAwait(false);

        var result = 0;
        foreach (var task in dispatchTasks)
        {
            result += task.IsCompleted ? task.Result : await task.ConfigureAwait(false);
            task.Dispose();
        }

        return result;
    }

    public async Task<int> CollectOrderbooks(Action? orderbookContinuation = null,
        TimeSpan downloadingTimeout = default, CancellationToken cancellationToken = default)
    {
        using var dm = new DisposableManager();
        await using var container = _container.GetNestedContainer();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(downloadingTimeout);

        var cancellationTokenWithTimeout = cts.Token;
        var instruments = await ListInstruments(container, cancellationTokenWithTimeout).ConfigureAwait(false);
        var maxSubscriptionPerWebsocket = _options.MaxSubscriptionPerWebsocket;
        List<List<OkxOrderbookSubscription>> subscriptions = new()
            { new List<OkxOrderbookSubscription>(Math.Min(maxSubscriptionPerWebsocket, _previousInstrumentCount)) };


        var lastSubscriptions = subscriptions[^1];
        foreach (var instrument in instruments)
        {
            if (lastSubscriptions.Count >= maxSubscriptionPerWebsocket)
            {
                lastSubscriptions = new List<OkxOrderbookSubscription>(maxSubscriptionPerWebsocket);
                subscriptions.Add(lastSubscriptions);
            }

            lastSubscriptions.Add(new OkxOrderbookSubscription(instrumentId: instrument));
        }

        var numWebsocket = subscriptions.Count;
        var tasks = new List<Task>(numWebsocket);
        var backgroundTasks = new List<Task>(numWebsocket);
        List<OkxWebsocketForOrderbook> websockets = new(numWebsocket);

        var bufferBlock = new BufferBlock<OkxWebsocketOrderbookResponse>(new DataflowBlockOptions()
            { BoundedCapacity = instruments.Count });


        var processBufferBlockTask = Task.FromResult(0);
        try
        {
            var pool = container.GetInstance<IOkxWebsocketPool>();

            async Task ConnectAndProcessSubscriptions<TCollection>(OkxWebsocketForOrderbook ws,
                TCollection subscriptions, CancellationToken cancellationToken)
                where TCollection : IReadOnlyCollection<OkxSubscription>
            {
                await pool.TryInjectWebsocket(ws, cancellationToken).ConfigureAwait(false);
                await ws.ConnectIfNeeded(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                backgroundTasks.Add(ws.EnsureConnectionActivityTask(cancellationToken));
                backgroundTasks.Add(ws.ReceiveLoop(cancellationToken));
                if (await ws.Subscribe(subscriptions, cancellationToken).ConfigureAwait(false) != subscriptions.Count)
                {
                    _logger.Error("unable to subscribe to {Count} orderbook", subscriptions.Count);
                }
            }

            for (var i = 0; i < numWebsocket; i++)
            {
                var ws = container.GetInstance<OkxWebsocketForOrderbook>();
                websockets.Add(ws);
                dm.LinkDisposable(ws);
                ws.AddBufferBlock(bufferBlock);
                if (backgroundTasks.Count == 0)
                {
                    backgroundTasks.Add(pool.BackgroundLoop(cancellationTokenWithTimeout));
                }

                tasks.Add(ConnectAndProcessSubscriptions(ws, subscriptions[i], cancellationTokenWithTimeout));
            }

            processBufferBlockTask = ProcessBufferBlock(instruments.Count, container, bufferBlock, cancellationToken);
            while (tasks.Count > 0 && !cancellationTokenWithTimeout.IsCancellationRequested)
            {
                try
                {
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                    break;
                }
                catch
                {
                    for (var i = 0; i < tasks.Count; i++)
                    {
                        var task = tasks[i];
                        if (task.IsCompletedSuccessfully)
                        {
                            continue;
                        }

                        if (task.IsCompleted)
                        {
                            if (task.Exception is { InnerExceptions.Count: > 0 })
                            {
                                _logger.Error(task.Exception, "Task {Name} n°{Number} failed",
                                    nameof(ConnectAndProcessSubscriptions), i);
                            }

                            if (task.IsCanceled && !cancellationTokenWithTimeout.IsCancellationRequested)
                            {
                                _logger.Warning("Task {Name} n°{Number} cancelled earlier than expected",
                                    nameof(ConnectAndProcessSubscriptions), i);
                            }

                            tasks[i] = Task.CompletedTask;
                            task.Dispose();
                        }
                    }
                }
            }

            try
            {
                await Task.WhenAny(Task.WhenAll(backgroundTasks), processBufferBlockTask)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                var log = true;
                if (e is OperationCanceledException ||
                    (e is AggregateException agg && agg.InnerExceptions.All(exception =>
                        exception is OperationCanceledException)))
                {
                    log = false;
                    var cancellationRequested = cts.IsCancellationRequested;

                    if (!cancellationRequested)
                    {
                        log = true;
                        cts.Cancel();
                    }
                }

                if (log)
                {
                    _logger.Error(e, "unexpected error");
                }
            }
        }
        finally
        {
            cts.Cancel();

            foreach (var ws in websockets)
            {
                await ws.DisposeAsync().ConfigureAwait(false);
                dm.UnlinkDisposable(ws);
            }

            foreach (var task in tasks)
            {
                if (ReferenceEquals(task, processBufferBlockTask))
                {
                    continue;
                }

                if (!task.IsCompleted)
                {
                    await task.ConfigureAwait(false);
                }

                task.Dispose();
            }

            foreach (var task in backgroundTasks)
            {
                if (ReferenceEquals(task, processBufferBlockTask))
                {
                    continue;
                }

                if (!task.IsCompleted)
                {
                    await task.ConfigureAwait(false);
                }

                task.Dispose();
            }
            
            orderbookContinuation?.Invoke();

            if (!processBufferBlockTask.IsCompleted && !cancellationToken.IsCancellationRequested)
            {
                await processBufferBlockTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            bufferBlock.Complete();
        }

        return processBufferBlockTask.IsCompleted
            ? processBufferBlockTask.Result
            : await processBufferBlockTask.ConfigureAwait(false);
    }
}