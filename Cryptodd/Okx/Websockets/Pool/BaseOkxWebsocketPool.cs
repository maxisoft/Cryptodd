using System.Collections.Concurrent;
using System.Net.WebSockets;
using Cryptodd.Okx.Limiters;
using Cryptodd.Okx.Models;
using Lamar;
using Maxisoft.Utils.Collections.Queues;
using Maxisoft.Utils.Objects;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Okx.Websockets.Pool;

public abstract class BaseOkxWebsocketPool<TOption> : IDisposable, IOkxWebsocketPool
    where TOption : OkxWebsocketPoolOptions, new()
{
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly INestedContainer _container;
    private readonly IOkxLimiterRegistry _limiterRegistry;
    private readonly ConcurrentQueue<Task> _linkedTaskQueue = new();
    private readonly TOption _options = new();
    private readonly Deque<OkxWebsocketPoolEntry> _websocketPoolEntries = new();

    protected BaseOkxWebsocketPool(ILogger logger, IContainer container, IConfiguration configuration,
        IOkxLimiterRegistry limiterRegistry,
        Boxed<CancellationToken> cancellationToken)
    {
        _limiterRegistry = limiterRegistry;
        Logger = logger.ForContext(GetType());
        _container = container.GetNestedContainer();
        configuration.Bind(_options);
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    }


    protected ILogger Logger { get; }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public async Task BackgroundLoop(CancellationToken cancellationToken)
    {
        using var cts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellationTokenSource.Token);
        cancellationToken = cts.Token;
        var task = InnerBackgroundLoop(cancellationToken);
        CleanupLinkedTasks();
        _linkedTaskQueue.Enqueue(task);
        try
        {
            await task.ConfigureAwait(false);
        }
        finally
        {
            CleanupLinkedTasks();
        }
    }

    public async ValueTask<bool> TryInjectWebsocket<T, TData2, TOptions2>(T other, CancellationToken cancellationToken)
        where T : BaseOkxWebsocket<TData2, TOptions2>
        where TData2 : PreParsedOkxWebSocketMessage, new()
        where TOptions2 : BaseOkxWebsocketOptions, new()
    {
        await Tick(cancellationToken).ConfigureAwait(false);
        OkxWebsocketPoolEntry? entry = null;
        var maxIter = _options.MaxCapacity;
        while (entry is null)
        {
            lock (_websocketPoolEntries)
            {
                if (!_websocketPoolEntries.TryPopBack(out entry))
                {
                    return false;
                }
            }


            if (IsUnusable(entry))
            {
                await entry.DisposeAsync().ConfigureAwait(false);
                entry = null;
            }

            if (maxIter-- < 0)
            {
                return false;
            }
        }

        var res = true;
        try
        {
            res = await entry.Websocket.SwapWebSocket<T, TData2, TOptions2>(other, cancellationToken)
                .ConfigureAwait(false);
            return res;
        }
        finally
        {
            if (!res && !IsUnusable(entry))
            {
                lock (_websocketPoolEntries)
                {
                    _websocketPoolEntries.PushBack(entry);
                }
            }
            else
            {
                await entry.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task Tick(CancellationToken cancellationToken)
    {
        FastCleanupLinkedTasks();
        if (_cancellationTokenSource.IsCancellationRequested)
        {
            return;
        }

        await FastCleanupPoolEntries().ConfigureAwait(false);

        if (Count >= _options.MaxCapacity)
        {
            return;
        }

        var limiter = _limiterRegistry.WebsocketConnectionLimiter;
        if (limiter.AvailableCount <= 0)
        {
            await limiter.TriggerOnTick().ConfigureAwait(false);
            if (limiter.AvailableCount <= 0)
            {
                return;
            }
        }


        var cts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token);
        try
        {
            var ws = await CreateNewWebsocket(cancellationToken).ConfigureAwait(false);
            try
            {
                await ws.ConnectIfNeeded(cancellationToken).ConfigureAwait(false);
                var entry = new OkxWebsocketPoolEntry
                {
                    Websocket = ws,
                    ActivityTask = ws.EnsureConnectionActivityTask(cts.Token),
                    CancellationTokenSource = cts
                };
                lock (_websocketPoolEntries)
                {
                    _websocketPoolEntries.PushBack(entry);
                }

                Logger.Verbose("new ws in pool");
            }
            catch
            {
                await ws.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch (Exception e)
        {
            cts.Dispose();
            if (e is not OperationCanceledException)
            {
                Logger.Warning(e, "unable to create ws in pool");
            }

            throw;
        }
    }

    public async ValueTask<bool> Return<T, TData2, TOptions2>(T ws, CancellationToken cancellationToken)
        where T : BaseOkxWebsocket<TData2, TOptions2>
        where TData2 : PreParsedOkxWebSocketMessage, new()
        where TOptions2 : BaseOkxWebsocketOptions, new()
    {
        if (Count >= _options.MaxCapacity)
        {
            await FastCleanupPoolEntries().ConfigureAwait(false);
            if (Count >= _options.MaxCapacity)
            {
                return false;
            }
        }


        var entryWs = await CreateNewWebsocket(cancellationToken).ConfigureAwait(false);
        CancellationTokenSource cts;
        try
        {
            if (!await entryWs.SwapWebSocket<T, TData2, TOptions2>(ws, cancellationToken).ConfigureAwait(false))
            {
                await entryWs.DisposeAsync().ConfigureAwait(false);
                return false;
            }

            cts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token);
        }
        catch
        {
            await entryWs.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        try
        {
            var entry = new OkxWebsocketPoolEntry
            {
                Websocket = entryWs,
                ActivityTask = ws.EnsureConnectionActivityTask(cts.Token),
                CancellationTokenSource = cts
            };
            try
            {
                lock (_websocketPoolEntries)
                {
                    if (Count >= _options.MaxCapacity)
                    {
                        entry.Dispose();
                        return false;
                    }

                    _websocketPoolEntries.PushBack(entry);
                    return true;
                }
            }
            catch
            {
                await entry.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch
        {
            cts.Dispose();
            throw;
        }
    }

    // ReSharper disable once InconsistentlySynchronizedField
    public int Count => _websocketPoolEntries.Count;

    protected virtual ValueTask<PooledOkxWebsocket> CreateNewWebsocket(CancellationToken cancellationToken) =>
        ValueTask.FromResult(_container.GetInstance<PooledOkxWebsocket>());

    protected virtual async Task InnerBackgroundLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Tick(cancellationToken).ConfigureAwait(false);
            await Task.Delay(_options.BackgroundTaskInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private void FastCleanupLinkedTasks()
    {
        if (_linkedTaskQueue.TryDequeue(out var task))
        {
            if (!task.IsCompleted)
            {
                _linkedTaskQueue.Enqueue(task);
            }
        }
    }

    private void CleanupLinkedTasks()
    {
        Task? first = null;
        while (_linkedTaskQueue.TryDequeue(out var task))
        {
            if (first is not null)
            {
                if (ReferenceEquals(task, first))
                {
                    break;
                }
            }
            else
            {
                first ??= task;
            }

            if (task.IsCompleted)
            {
                continue;
            }

            _linkedTaskQueue.Enqueue(task);
        }
    }

    private ValueTask FastCleanupPoolEntries()
    {
        var count = Count;
        if (count == 0)
        {
            return ValueTask.CompletedTask;
        }

        lock (_websocketPoolEntries)
        {
            if (!_websocketPoolEntries.TryPopFront(out var entry))
            {
                return ValueTask.CompletedTask;
            }

            if (IsUnusable(entry))
            {
                return entry.DisposeAsync();
            }

            _websocketPoolEntries.PushFront(entry);

            if (count == 1)
            {
                return ValueTask.CompletedTask;
            }

            if (!_websocketPoolEntries.TryPopBack(out entry))
            {
                return ValueTask.CompletedTask;
            }

            if (IsUnusable(entry))
            {
                return entry.DisposeAsync();
            }

            _websocketPoolEntries.PushBack(entry);
        }

        return ValueTask.CompletedTask;
    }

    private static bool IsUnusable(OkxWebsocketPoolEntry entry)
    {
        return entry.Websocket.State is not WebSocketState.Open || entry.ActivityTask.IsCompleted ||
               entry.ElapsedSinceCreation > 30_000;
    }

    protected virtual void Dispose(bool disposing)
    {
        _cancellationTokenSource.Cancel();
        if (disposing)
        {
            var cpy = Array.Empty<OkxWebsocketPoolEntry>();
            lock (_websocketPoolEntries)
            {
                if (!_websocketPoolEntries.IsEmpty)
                {
                    cpy = _websocketPoolEntries.ToArray();
                    _websocketPoolEntries.Clear();
                }
            }

            foreach (var poolEntry in cpy)
            {
                poolEntry.Dispose();
            }


            while (_linkedTaskQueue.TryDequeue(out var task))
            {
                task.Wait();
                task.Dispose();
            }

            _cancellationTokenSource.Dispose();
            _container.Dispose();
        }
    }
}