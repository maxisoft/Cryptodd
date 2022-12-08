using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using Cryptodd.Http;
using Cryptodd.IoC;
using Lamar;
using LamarCodeGeneration;
using Maxisoft.Utils.Algorithms;
using Maxisoft.Utils.Collections.LinkedLists;
using Maxisoft.Utils.Logic;
using Maxisoft.Utils.Objects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Cryptodd.Bitfinex.WebSockets;

public class BitfinexPublicWebSocketPoolOptions
{
    /// <summary>
    /// set accordingly to <a href="https://docs.bitfinex.com/docs/requirements-and-limitations">requirements-and-limitations</a>
    /// </summary>
    /// 
    public float ConnectionPerMinute { get; set; } = 20;

    public int DefaultSocketInPool { get; set; } = 3;
    public int MaxSocketInPool { get; set; } = 7;
}

internal sealed class ConnectionLimiter
{
    public int Limit { get; internal set; }
    private int _counter;
    public int Available => Math.Max(Limit - _counter, 0);
    private readonly Stopwatch _stopwatch = new();
    private readonly object _lockObject = new();
    private readonly ConcurrentQueue<TaskCompletionSource> _taskCompletionSourceQueue = new();
    public int WaiterCount => _taskCompletionSourceQueue.Count;

    public ConnectionLimiter(int limit)
    {
        Limit = limit;
        _stopwatch.Restart();
        _counter = 0;
    }

    private bool Notify()
    {
        bool? res = null;
        while (_taskCompletionSourceQueue.TryDequeue(out var tcs))
        {
            var r = tcs.TrySetResult();
            res = res.GetValueOrDefault(r) && r;
        }

        return res.GetValueOrDefault();
    }

    public Task Wait()
    {
        var tcs = new TaskCompletionSource();
        _taskCompletionSourceQueue.Enqueue(tcs);
        return tcs.Task;
    }

    public bool TryRegisterConnection()
    {
        HandlePeriodicReset();

        if (_counter > Limit)
        {
            return false;
        }

        Interlocked.Increment(ref _counter);

        return true;
    }

    private void HandlePeriodicReset(bool notify = true)
    {
        if (!_stopwatch.IsRunning)
        {
            _stopwatch.Restart();
        }

        if (_stopwatch.Elapsed <= TimeSpan.FromMinutes(1))
        {
            return;
        }

        _stopwatch.Restart();
        _counter = 0;
        if (notify)
        {
            Notify();
        }
    }

    public void OnConnectionReturned(bool decrement)
    {
        HandlePeriodicReset(false);
        if (decrement)
        {
            _counter = Math.Max(_counter - 1, 0);
        }

        if (_counter < Limit)
        {
            Notify();
        }
    }
}

public class RentedBitfinexPublicWebSocket : BitfinexPublicWebSocket
{
    internal BitfinexPublicWebSocketPool? Pool { get; set; }
    private readonly AtomicBoolean _safeToReuse = new AtomicBoolean();

    private readonly ConcurrentBag<ClientWebSocket> _webSockets = new();

    public RentedBitfinexPublicWebSocket(IClientWebSocketFactory webSocketFactory, IConfiguration configuration,
        ILogger logger, Boxed<CancellationToken> cancellationToken) : base(webSocketFactory, configuration, logger,
        cancellationToken) { }


    protected override async ValueTask<ClientWebSocket> CreateWebSocket()
    {
        ClientWebSocket? res = null;

        if (Pool is not null)
        {
            res = await Pool.RentWebSocket(CancellationToken);
        }

        res ??= await base.CreateWebSocket();

        _webSockets.Add(res);
        return res;
    }

    protected override ValueTask CloseWebSocketAsync(ClientWebSocket ws) => ValueTask.CompletedTask;

    protected internal override void Close()
    {
        if (Pool is null)
        {
            base.Close();
            return;
        }

        try
        {
            while (_webSockets.TryTake(out var ws))
            {
                Pool.ReturnWebSocket(ws, _safeToReuse.Value && SubscriptionsCount <= 0);
            }
        }
        catch
        {
            base.Close();
            throw;
        }
    }

    protected override void Dispose(bool disposing)
    {
        _safeToReuse.FalseToTrue();
        try
        {
            base.Dispose(disposing);
        }
        finally
        {
            _safeToReuse.TrueToFalse();
        }
    }
}

[Singleton]
public class BitfinexPublicWebSocketPool : IService, IDisposable
{
    private readonly ILogger _logger;
    private readonly BitfinexPublicWebSocketPoolOptions _options = new();
    private readonly ConnectionLimiter _connectionLimiter;
    private readonly Lazy<INestedContainer> _nestedContainer;
    private readonly LinkedListAsIList<ClientWebSocket> _webSockets = new();
    private readonly LinkedListAsIList<WeakReference> _subscribers = new LinkedListAsIList<WeakReference>();
    private float _dynamicWebsocketCount;

    public int AvailableWebsocketCount
    {
        get
        {
            var res = _webSockets.Count + _connectionLimiter.Available;
            return res > 0 ? res : Math.Max((int)_dynamicWebsocketCount, 0);
        }
    }

    public BitfinexPublicWebSocketPool(ILogger logger, IContainer container, IConfiguration configuration)
    {
        _logger = logger;
        configuration.GetSection("Bitfinex:Websocket:Pool").Bind(_options);
        _connectionLimiter = new ConnectionLimiter(checked((int)(uint)_options.ConnectionPerMinute));
        _nestedContainer = new Lazy<INestedContainer>(container.GetNestedContainer);
        _dynamicWebsocketCount = Math.Max(_options.DefaultSocketInPool, 1);
    }


    /// <summary>
    /// Return a pooled websocket or wait until caller could create a new one
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns>an opened websocket or <c>null</c></returns>
    internal async ValueTask<ClientWebSocket?> RentWebSocket(CancellationToken cancellationToken)
    {
        Maintain();

        var res = GetOpenedWebSocket();
        if (res is not null)
        {
            return res;
        }

        var increment = true;
        while (!_connectionLimiter.TryRegisterConnection())
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(1000);
            try
            {
                await Task.WhenAny(_connectionLimiter.Wait().WaitAsync(cts.Token), Task.Delay(1000, cancellationToken));
            }
            catch (Exception e) when (e is OperationCanceledException or TimeoutException)
            {
                _logger.Verbose(e, "Polling _connectionLimiter");
            }

            res = GetOpenedWebSocket();
            if (res is not null)
            {
                return res;
            }
        }

        try
        {
            return res;
        }
        catch
        {
            _connectionLimiter.OnConnectionReturned(true);
            throw;
        }
        finally
        {
            _dynamicWebsocketCount = (increment
                    ? (_dynamicWebsocketCount + 1)
                    : (_webSockets.Count > 0 ? _dynamicWebsocketCount - 0.1f : _dynamicWebsocketCount))
                .Clamp(_options.DefaultSocketInPool, _options.MaxSocketInPool);
        }
    }

    private ClientWebSocket? GetOpenedWebSocket()
    {
        // ReSharper disable once InconsistentlySynchronizedField
        if (_webSockets.Count == 0)
        {
            return null;
        }

        lock (_webSockets)
        {
            var node = _webSockets.First;
            while (node is not null)
            {
                var ws = node.Value;
                if (ws.State == WebSocketState.Open)
                {
                    _webSockets.Remove(node);
                    return ws;
                }

                node = node.Next;
            }
        }


        return null;
    }

    public async ValueTask<RentedBitfinexPublicWebSocket> GetWebSocket(IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Maintain();

        var ws = serviceProvider.GetRequiredService<RentedBitfinexPublicWebSocket>();
        try
        {
            ws.Pool = this;
        }
        catch
        {
            await ws.DisposeAsync();
            throw;
        }

        return ws;
    }

    private void Maintain()
    {
        _connectionLimiter.Limit = checked((int)(uint)_options.ConnectionPerMinute);
        // ReSharper disable once InconsistentlySynchronizedField
        if (_webSockets.Count <= 0)
        {
            return;
        }

        lock (_webSockets)
        {
            var node = _webSockets.First;
            while (node is not null)
            {
                var ws = node.Value;
                var next = node.Next;
                if (ws.State != WebSocketState.Open)
                {
                    ws.Abort();
                    ws.Dispose();

                    _webSockets.Remove(node);
                }

                node = next;
            }
        }
    }

    public void ReturnWebSocket(ClientWebSocket webSocket, bool reusable)
    {
        // ReSharper disable once InconsistentlySynchronizedField
        if (!reusable || webSocket.State != WebSocketState.Open || _webSockets.Count >= _dynamicWebsocketCount)
        {
            webSocket.Abort();
            webSocket.Dispose();
            lock (_webSockets)
            {
                _webSockets.Remove(webSocket);
            }
        }

        else
        {
            lock (_webSockets)
            {
                _webSockets.AddFirst(webSocket);
            }
        }

        _connectionLimiter.OnConnectionReturned(false);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
#if DEBUG
        _logger.Warning("Disposing {Name} singleton", GetType().NameInCode());
#endif
        if (_nestedContainer.IsValueCreated)
        {
            _nestedContainer.Value.Dispose();
        }

        lock (_webSockets)
        {
            foreach (var webSocket in _webSockets)
            {
                webSocket.Dispose();
            }

            _webSockets.Clear();
            _dynamicWebsocketCount = _options.DefaultSocketInPool;
        }
    }

    # region Subscriber

    public int SubscriberCount => _subscribers.Count;

    public bool AddSubscriber(object subscriber)
    {
        var node = _subscribers.First;
        while (node is not null)
        {
            var next = node.Next;
            if (node.Value.IsAlive)
            {
                if (ReferenceEquals(node.Value.Target, subscriber))
                {
                    return false;
                }
            }
            else
            {
                lock (_subscribers)
                {
                    _subscribers.Remove(node);
                }
            }


            node = next;
        }

        lock (_subscribers)
        {
            _subscribers.AddLast(new WeakReference(subscriber));
        }

        return true;
    }

    public bool RemoveSubscriber(object subscriber)
    {
        var node = _subscribers.First;
        while (node is not null)
        {
            var next = node.Next;
            if (node.Value.IsAlive)
            {
                if (ReferenceEquals(node.Value.Target, subscriber))
                {
                    lock (_subscribers)
                    {
                        _subscribers.Remove(node);
                    }

                    return true;
                }
            }
            else
            {
                lock (_subscribers)
                {
                    _subscribers.Remove(node);
                }
            }


            node = next;
        }

        return false;
    }

    #endregion
}