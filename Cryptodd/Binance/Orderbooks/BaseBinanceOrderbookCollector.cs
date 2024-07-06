using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using Cryptodd.Binance.Http;
using Cryptodd.Binance.Http.Options;
using Cryptodd.Binance.Http.RateLimiter;
using Cryptodd.Binance.Models;
using Cryptodd.Binance.Orderbooks.Handlers;
using Cryptodd.Binance.Orderbooks.Websockets;
using Cryptodd.IoC;
using Cryptodd.Pairs;
using Cryptodd.Utils;
using JasperFx.Core.Reflection;
using Lamar;
using MathNet.Numerics.Random;
using Maxisoft.Utils.Collections.Lists;
using Maxisoft.Utils.Disposables;
using Maxisoft.Utils.Objects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;

namespace Cryptodd.Binance.Orderbooks;

public abstract class
    BaseBinanceOrderbookCollector<
        TBinanceOrderbookWebsocket,
        TOptions,
        TOrderbookHttpCallOptions,
        TBinanceWebsocketCollection> :
    IAsyncDisposable,
    IService
    where TOptions : BaseBinanceOrderbookWebsocketOptions, new()
    where TBinanceOrderbookWebsocket : BaseBinanceOrderbookWebsocket<TOptions>
    where TOrderbookHttpCallOptions : IBinancePublicHttpApiCallOptions, new()
    where TBinanceWebsocketCollection : BaseBinanceWebsocketCollection<TBinanceOrderbookWebsocket, TOptions>, new()
{
    private readonly Stopwatch _cachedSymbolsStopwatch = new();

    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly IContainer _container;
    private readonly DisposableManager _disposableManager = new();

    private readonly Stopwatch _expiryCleanupStopwatch = new();

    private readonly ConcurrentBag<TaskCompletionSource<string>> _newPendingSymbolForHttpCompletionSources = new();

    private readonly HashSet<string> _pendingSymbolsForHttp = new(StringComparer.InvariantCultureIgnoreCase);

    private readonly Xoshiro256StarStar _random = new(true);
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
    private Task? _backgroundTasks;
    private ArrayList<string> _cachedSymbols = new();

    private Task _websocketTask = Task.CompletedTask;

    protected BaseBinanceOrderbookCollector(IContainer container, ILogger logger,
        IConfiguration configuration, Boxed<CancellationToken> cancellationToken)
    {
        _container = container;
        Logger = logger.ForContext(GetType());
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    }

    protected ILogger Logger { get; }

    protected BufferBlock<ReferenceCounterDisposable<CombinedStreamEnvelope<DepthUpdateMessage>>> TargetBlock { get; } =
        new(new DataflowBlockOptions { EnsureOrdered = true, BoundedCapacity = 8192 });

    private INestedContainer? NestedContainer { get; set; }

    private OrderbookCollection Orderbooks { get; } = new();

    protected BinanceOrderbookCollectorOptions Options { get; init; } = new();

    protected TBinanceWebsocketCollection Websockets { get; set; } = new();

    protected string ConfigurationSection { get; init; } = "";
    protected string PairFilterName { get; init; } = "";
    protected abstract IBinanceHttpOrderbookProvider HttpOrderbookProvider { get; }
    protected abstract IBinanceHttpSymbolLister SymbolLister { get; }

    protected abstract int MaxOrderBookLimit { get; }

    protected abstract IBinanceRateLimiter RateLimiter { get; }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsync(true);
        GC.SuppressFinalize(this);
    }

    protected abstract TBinanceWebsocketCollection CreateWebsockets(ArrayList<string> symbols,
        IServiceProvider serviceProvider);

    protected virtual async Task DisposeAsync(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        TargetBlock.Complete();
        await _cancellationTokenSource.CancelAsync();
        await Websockets.DisposeAsync();
        _disposableManager.Dispose();
        _websocketTask.Dispose();
        await (NestedContainer?.DisposeAsync() ?? ValueTask.CompletedTask);
        _semaphoreSlim.Dispose();
        _cancellationTokenSource.Dispose();
    }

    protected virtual async Task Setup(CancellationToken cancellationToken)
    {
        var updateSymbolsTask = UpdateCachedSymbols(cancellationToken);
        if (_cachedSymbols.Count <= 0)
        {
            await updateSymbolsTask.ConfigureAwait(true);
        }

        var symbols = _cachedSymbols;
        NestedContainer ??= _container.GetNestedContainer();
        var container = NestedContainer;

        var webSockets = Websockets;

        bool ShouldRecreate()
        {
            return webSockets.Count == 0 || _websocketTask.IsCompleted || !Websockets.SymbolHashMatch(symbols);
        }

        if (ShouldRecreate())
        {
            await _semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (ShouldRecreate())
                {
                    if (_websocketTask.IsFaulted)
                    {
                        Logger.Error(_websocketTask.Exception, "error with previous websocket tasks");
                    }

                    await webSockets.DisposeAsync();
                    await container.DisposeAsync();
                    NestedContainer = _container.GetNestedContainer();
                    container = NestedContainer;
                    webSockets = CreateWebsockets(symbols, container);
                    Logger.Debug("Created {Count} websockets for {SymbolCount} symbols", webSockets.Count,
                        symbols.Count);
                    _websocketTask.Dispose();
                    Websockets = webSockets;
                    _websocketTask = webSockets.Start();
                }
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }


        if (!updateSymbolsTask.IsCompleted)
        {
            await updateSymbolsTask;
        }

        if (_backgroundTasks is null || _backgroundTasks.IsCompleted)
        {
            await _semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_backgroundTasks is null || _backgroundTasks.IsCompleted)
                {
                    if (_backgroundTasks?.IsFaulted ?? false)
                    {
                        Logger.Error(_backgroundTasks.Exception, "{Collector} background tasks faulted", GetType());
                    }

                    _backgroundTasks?.Dispose();
                    Logger.Verbose("Starting background tasks for {Name}", GetType().NameInCode());
                    _backgroundTasks = StartBackgroundTasks();
                }
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }
    }

    private async Task ProcessUpdates(CancellationToken cancellationToken)
    {
        while (await TargetBlock.OutputAvailableAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TargetBlock.TryReceiveAll(out var items))
            {
                continue;
            }

            using var dm = new DisposableManager();

            foreach (var reference in items)
            {
                dm.LinkDisposable(reference);
            }

            void UpdateOrderbook(
                ReferenceCounterDisposable<CombinedStreamEnvelope<DepthUpdateMessage>> referenceCounterDisposable)
            {
                var depthUpdateMessage = referenceCounterDisposable.ValueRef.Data;
                var symbol = depthUpdateMessage.Symbol;
                var orderbook = Orderbooks[symbol];
                if ((depthUpdateMessage.PreviousUpdateId is null &&
                     depthUpdateMessage.FirstUpdateId - orderbook.LastUpdateId > 1) ||
                    (depthUpdateMessage.PreviousUpdateId ?? orderbook.LastUpdateId) != orderbook.LastUpdateId ||
                    orderbook.IsEmpty())
                {
                    ScheduleSymbolForHttpUpdate(symbol);
                }

                lock (orderbook)
                {
                    orderbook.Update(in depthUpdateMessage);
                }
            }

            if (items.Count > 32)
            {
                Parallel.ForEach(items, UpdateOrderbook);
            }
            else
            {
                foreach (var reference in items)
                {
                    UpdateOrderbook(reference);
                }
            }

            if (items.Count > 0)
            {
                await Task.Delay(10, cancellationToken);
            }
        }
    }

    private void ScheduleSymbolForHttpUpdate(string symbol, LogEventLevel logLevel = LogEventLevel.Debug)
    {
        if (string.IsNullOrEmpty(symbol))
        {
            throw new ArgumentNullException(nameof(symbol), "Empty symbol not allowed");
        }

        lock (_pendingSymbolsForHttp)
        {
            if (_pendingSymbolsForHttp.Add(symbol))
            {
                Logger.Write(logLevel, "Scheduling orderbook http update for {Symbol}", symbol);
                while (_newPendingSymbolForHttpCompletionSources.TryTake(out var tcs))
                {
                    tcs.TrySetResult(symbol);
                }
            }
        }
    }

    protected virtual async Task StartBackgroundTasks()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token);
        var tasks = Array.Empty<Task>();
        try
        {
            tasks = new[] { PollPendingSymbolsForHttp(cts.Token), ProcessUpdates(cts.Token) };
            await Task.WhenAny(tasks).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            await cts.CancelAsync();
            foreach (var task in tasks)
            {
                try
                {
                    await task.WaitAsync(_cancellationTokenSource.Token);
                }
                catch (Exception exception)
                {
                    Logger.Verbose(exception, "");
                }
            }

            if (e is ObjectDisposedException or OperationCanceledException or TaskCanceledException)
            {
                try
                {
                    if (!_cancellationTokenSource.IsCancellationRequested)
                    {
                        throw;
                    }
                }
                catch (ObjectDisposedException e2)
                {
                    Logger.Error(e2, "{Cts} disposed, assuming task is cancelled", nameof(_cancellationTokenSource));
                }
            }
            else
            {
                throw;
            }
        }
    }

    protected virtual async Task PollPendingSymbolsForHttp(CancellationToken cancellationToken)
    {
        const int expWaitInitialValue = 1000;
        var expWait = expWaitInitialValue;
        var httpCallOption = new TOrderbookHttpCallOptions();
        var prevSymbol = "";
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            var availableWeight = RateLimiter.AvailableWeight;
            var callWeight = httpCallOption.ComputeWeight(MaxOrderBookLimit);
            Debug.Assert(callWeight > 0, "callWeight > 0");

            if (availableWeight <= callWeight)
            {
                expWait += expWait;
                expWait = Math.Max(expWait, 60_000);
                await Task.Delay(expWait, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (availableWeight < _random.Next(1, 5) * callWeight)
            {
                await Task.Delay(expWait, cancellationToken).ConfigureAwait(false);
                continue;
            }

            expWait = expWaitInitialValue;

            string symbol;
            // ReSharper disable once InconsistentlySynchronizedField
            if (_pendingSymbolsForHttp.Count == 0)
            {
                symbol = "";
            }
            else
            {
                lock (_pendingSymbolsForHttp)
                {
                    symbol = _pendingSymbolsForHttp.FirstOrDefault("");
                    _pendingSymbolsForHttp.Remove(symbol);
                    if (!string.IsNullOrEmpty(symbol) && symbol == prevSymbol && _pendingSymbolsForHttp.Count > 0)
                    {
                        // try to pick a new symbol
                        symbol = _pendingSymbolsForHttp.FirstOrDefault("");
                        _pendingSymbolsForHttp.Remove(symbol);
                        Debug.Assert(!string.IsNullOrEmpty(prevSymbol));
                        _pendingSymbolsForHttp.Add(prevSymbol);
                    }
                }
            }


            if (string.IsNullOrWhiteSpace(symbol))
            {
                var tcs = new TaskCompletionSource<string>();
                _newPendingSymbolForHttpCompletionSources.Add(tcs);
                await Task.WhenAny(Task.Delay(expWaitInitialValue, cancellationToken), tcs.Task).ConfigureAwait(false);
                tcs.TrySetCanceled(cancellationToken);
                continue;
            }

            if (symbol == prevSymbol)
            {
                await Task.Delay(expWait, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                prevSymbol = symbol;
            }

            try
            {
                var orderbook = Orderbooks[symbol];
                bool prevSafeToRemoveEntries;
                lock (orderbook)
                {
                    prevSafeToRemoveEntries = orderbook.SafeToRemoveEntries;
                    orderbook.SafeToRemoveEntries = false;
                }

                try
                {
                    using var remoteOb =
                        await HttpOrderbookProvider.GetOrderbook(symbol, MaxOrderBookLimit,
                            cancellationToken).ConfigureAwait(false);

                    var dateTime = remoteOb.DateTime ?? DateTimeOffset.Now;

                    lock (orderbook)
                    {
                        orderbook.SafeToRemoveEntries = prevSafeToRemoveEntries;
                        orderbook.DropOutdated(remoteOb, Options.FullCleanupOrderbookOnReconnect, MaxOrderBookLimit);
                        orderbook.SafeToRemoveEntries = false;
                        try
                        {
                            orderbook.Update(in remoteOb, dateTime);
                        }
                        catch
                        {
                            orderbook.SafeToRemoveEntries = prevSafeToRemoveEntries;
                            throw;
                        }

                        orderbook.SafeToRemoveEntries = true;
                    }
                }
                finally
                {
                    lock (orderbook)
                    {
                        orderbook.SafeToRemoveEntries = prevSafeToRemoveEntries;
                    }
                }


                lock (_pendingSymbolsForHttp)
                {
                    _pendingSymbolsForHttp.Remove(symbol);
                }
            }
            catch (Exception e)
            {
                Debug.Assert(!string.IsNullOrEmpty(symbol));
                lock (_pendingSymbolsForHttp)
                {
                    _pendingSymbolsForHttp.Add(symbol);
                }

                Logger.Error(e, "Unable to update orderbook for {Symbol} from http", symbol);
            }
        }
    }


    protected virtual async Task UpdateCachedSymbols(CancellationToken cancellationToken)
    {
        if (!_cachedSymbolsStopwatch.IsRunning || _cachedSymbolsStopwatch.Elapsed > Options.SymbolsExpiry)
        {
            var symbols = await ListSymbols(cancellationToken).ConfigureAwait(false);
            await using var container = _container.GetNestedContainer();
            var pairFilterLoader = container.GetRequiredService<IPairFilterLoader>();
            var pairFilter = await pairFilterLoader.GetPairFilterAsync(PairFilterName, cancellationToken);

            static ArrayList<string> FilterSymbols(List<string> symbols, IPairFilter pairFilter)
            {
                var res = new ArrayList<string>(symbols.Count);
                // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
                foreach (var symbol in symbols)
                {
                    if (pairFilter.Match(symbol))
                    {
                        res.Add(symbol);
                    }
                }

                return res;
            }

            _cachedSymbols = FilterSymbols(symbols, pairFilter);
            _cachedSymbols.ShrinkToFit();
            _cachedSymbolsStopwatch.Restart();
        }
    }

    private async Task<List<string>> ListSymbols(CancellationToken cancellationToken) =>
        await SymbolLister.ListSymbols(false, true, cancellationToken);

    public async Task CollectOrderBook(CancellationToken cancellationToken)
    {
        await Setup(cancellationToken);

        foreach (var symbol in Orderbooks)
        {
            var rawOb = Orderbooks[symbol];
            if (rawOb.IsEmpty())
            {
                continue;
            }

            {
                InMemoryOrderbook<OrderBookEntryWithStat>.SortedView? asks = null, bids = null;

                try
                {
                    lock (rawOb)
                    {
                        asks = rawOb.Asks;
                        asks.EnforceKeysEnumeration();
                        bids = rawOb.Bids;
                        bids.EnforceKeysEnumeration();
                    }

                    await DispatchToHandlers(symbol, asks, bids, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    asks?.Dispose();
                    bids?.Dispose();
                }
            }

            int askCount, bidCount;
            lock (rawOb)
            {
                rawOb.DropZeros();
                rawOb.ResetStatistics();
                if (Options.EntryExpiry <= TimeSpan.Zero)
                {
                    continue;
                }

                if (_expiryCleanupStopwatch.IsRunning &&
                    _expiryCleanupStopwatch.Elapsed <= Options.EntryExpiry / 10)
                {
                    continue;
                }

                (askCount, bidCount) = rawOb.DropOutdated(DateTimeOffset.Now - Options.EntryExpiry);
                _expiryCleanupStopwatch.Restart();
            }

            if (askCount + bidCount > 0)
            {
                ScheduleSymbolForHttpUpdate(symbol);
            }
        }
    }

    protected async ValueTask WaitForHandlers<T>(string name, IReadOnlyList<T> handlers, Task[] tasks,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch
        {
            for (var index = 0; index < tasks.Length; index++)
            {
                var task = tasks[index];
                try
                {
                    await task.ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    var verbosity = LogEventLevel.Error;
                    if (e is OperationCanceledException or TaskCanceledException &&
                        cancellationToken.IsCancellationRequested)
                    {
                        verbosity = LogEventLevel.Debug;
                    }

                    Logger.Write(verbosity, e, "{Handler} for {Name} threw up", handlers[index]?.GetType(), name);
                }
                finally
                {
                    task.Dispose();
                }
            }
        }
    }

    protected abstract ValueTask DispatchToObHandlers(IServiceContext container,
        BinanceOrderbookHandlerArguments arg,
        CancellationToken cancellationToken);

    protected abstract ValueTask CreateAggregateAndDispatch(IServiceContext container,
        BinanceOrderbookHandlerArguments arg,
        CancellationToken cancellationToken);

    private async ValueTask DispatchToHandlers(string symbol,
        InMemoryOrderbook<OrderBookEntryWithStat>.SortedView asks,
        InMemoryOrderbook<OrderBookEntryWithStat>.SortedView bids,
        CancellationToken cancellationToken)
    {
        Debug.Assert(ReferenceEquals(asks.Orderbook, bids.Orderbook));
        await using var container = _container.GetNestedContainer();
        var arg = new BinanceOrderbookHandlerArguments(symbol, asks, bids);
        await DispatchToObHandlers(container, arg, cancellationToken).ConfigureAwait(false);
        await CreateAggregateAndDispatch(container, arg, cancellationToken).ConfigureAwait(false);
    }
}