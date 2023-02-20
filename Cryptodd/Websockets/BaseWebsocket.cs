using System.Buffers;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using Cryptodd.Binance.Models;
using Cryptodd.Http;
using Maxisoft.Utils.Logic;
using Maxisoft.Utils.Objects;
using Serilog;
using Serilog.Events;

namespace Cryptodd.Websockets;

public interface IBaseWebsocketOptions
{
    public string BaseAddress { get; }
    public int ReceiveTimeout { get; }
    public int AdditionalReceiveBufferSize { get; }
    public int CloseConnectionTimeout { get; }
}

public abstract class BaseWebsocketOptions : IBaseWebsocketOptions
{
    public string BaseAddress { get; set; } = "";
    public int ReceiveTimeout { get; set; } = 60_000;

    public int AdditionalReceiveBufferSize { get; set; } = 128 << 10;

    public int CloseConnectionTimeout { get; set; } = 1000;
}

[Flags]
public enum ReceiveMessageFilter
{
    None = 0,
    ContinueFiltering = 1 << 0,
    ConsumeTheRest = 1 << 1,
    Ignore = 1 << 2,
    Break = 1 << 3,
    ForceDispatch = 1 << 4,
    ExtractedData = 1 << 5
}

public abstract class BaseWebsocket<TData, TOptions> : IDisposable, IAsyncDisposable
    where TOptions : IBaseWebsocketOptions
{
    protected ILogger Logger { get; set; }
    protected IClientWebSocketFactory WebSocketFactory { get; set; }
    protected abstract TOptions Options { get; set; }
    protected SemaphoreSlim SemaphoreSlim { get; } = new(1, 1);

    protected AtomicBoolean Disposed { get; } = new();
    protected abstract ClientWebSocket? WebSocket { get; set; }
    protected CancellationTokenSource LoopCancellationTokenSource { get; }
    protected CancellationToken CancellationToken => LoopCancellationTokenSource.Token;

    protected MemoryPool<byte> MemoryPool { get; set; } = MemoryPool<byte>.Shared;

    public long ConnectionCounter { get; protected set; }

    protected BaseWebsocket(ILogger logger, IClientWebSocketFactory webSocketFactory,
        Boxed<CancellationToken> cancellationToken)
    {
        Logger = logger.ForContext(GetType());
        WebSocketFactory = webSocketFactory;
        LoopCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    }

    public bool IsClosed
    {
        get
        {
            var ws = WebSocket;
            if (ws is null)
            {
                return true;
            }

            return ws.State switch
            {
                WebSocketState.Connecting => false,
                WebSocketState.Open => false,
                WebSocketState.CloseSent => false,
                _ => true
            };
        }
    }

    public WebSocketState State => WebSocket?.State ?? WebSocketState.None;

    public virtual void StopReceiveLoop(string reason = "", LogEventLevel logLevel = LogEventLevel.Information)
    {
        try
        {
            LoopCancellationTokenSource.Cancel();
        }
        catch (ObjectDisposedException e)
        {
            Logger.Verbose(e, "");
        }

        if (!string.IsNullOrEmpty(reason))
        {
            Logger.Write(logLevel, "Stopping websocket {Reason}", reason);
        }
    }

    protected abstract Uri CreateUri();

    public virtual async ValueTask<bool> ConnectIfNeeded(CancellationToken cancellationToken)
    {
        var res = false;
        if (!IsClosed)
        {
            return res;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, CancellationToken);
        cancellationToken = cts.Token;
        try
        {
            await SemaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException e)
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Logger.Verbose(e, "semaphore got disposed before WaitAsync() call");
                    return false;
                }
            }
            catch (ObjectDisposedException)
            {
                Logger.Verbose(e, "semaphore got disposed before WaitAsync() call");
                return false;
            }

            throw;
        }

        try
        {
            if (!IsClosed)
            {
                return false;
            }

            Close("");

            WebSocket = await WebSocketFactory
                .GetWebSocket(CreateUri(), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            res = true;
            ConnectionCounter += 1;
        }
        finally
        {
            try
            {
                SemaphoreSlim.Release();
            }
            catch (ObjectDisposedException e)
            {
                var level = Disposed ? LogEventLevel.Verbose : LogEventLevel.Error;
                Logger.Write(level, e, "semaphore got disposed before Release() call");
            }
        }

        return res;
    }

    protected virtual bool Dispose(bool disposing)
    {
        disposing &= Disposed.FalseToTrue();
        Close(disposing ? "disposing" : "");
        try
        {
            if (!LoopCancellationTokenSource.IsCancellationRequested)
            {
                LoopCancellationTokenSource.Cancel();
            }
        }
        catch (ObjectDisposedException e) // lgtm [cs/empty-catch-block]
        {
            if (disposing)
            {
                throw;
            }

            Debug.Write(e);
        }

        // ReSharper disable once InvertIf
        if (disposing)
        {
            SemaphoreSlim.Dispose();
            LoopCancellationTokenSource.Dispose();
        }

        return disposing;
    }

    private readonly object _lockObject = new();

    protected virtual void Close(string reason)
    {
        var ws = WebSocket;
        if (ws is null)
        {
            return;
        }

        lock (_lockObject)
        {
            ws = WebSocket;
            if (ws is null)
            {
                return;
            }

            try
            {
                ws.Abort();
                ws.Dispose();
                if (!string.IsNullOrEmpty(reason))
                {
                    Logger.Debug("Closing websocket due to {Reason}", reason);
                }
            }
            catch (ObjectDisposedException e)
            {
                Logger.Warning(e, "Error when closing ws");
            }

            WebSocket = null;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public async virtual ValueTask DisposeAsync(bool disposing)
    {
        try
        {
            LoopCancellationTokenSource.Cancel();
        }
        catch (ObjectDisposedException e)
        {
            Logger.Verbose(e, "unable to cancel {Name}", nameof(LoopCancellationTokenSource));
        }

        var ws = WebSocket;
        if (ws is { State: WebSocketState.Open })
        {
            using var closeCancellationToken = new CancellationTokenSource();
            if (Options.CloseConnectionTimeout > 0)
            {
                closeCancellationToken.CancelAfter(Options.CloseConnectionTimeout);
            }

            try
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, closeCancellationToken.Token);
            }
            catch (WebSocketException e)
            {
                if (ws.State is not (WebSocketState.Aborted or WebSocketState.Closed or WebSocketState.CloseReceived
                    or WebSocketState.CloseSent))
                {
                    Logger.Debug(e, "Error when closing ws");
                }
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
            {
                Logger.Debug(e, "Error when closing ws");
            }
        }

        Dispose(disposing);
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return DisposeAsync(true);
    }

    protected abstract ReceiveMessageFilter FilterReceivedMessage(Span<byte> message,
        ValueWebSocketReceiveResult receiveResult, ref TData? data);

    private readonly AsyncLocal<TData?> _dataLocal = new();

    protected TData? Data
    {
        get => _dataLocal.Value;
        set => _dataLocal.Value = value;
    }

    public async Task ReceiveLoop(CancellationToken cancellationToken)
    {
        await ReceiveLoop(false, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task ReceiveLoop(bool detached, CancellationToken cancellationToken)
    {
        try
        {
            while (!LoopCancellationTokenSource.IsCancellationRequested)
            {
                await ConnectIfNeeded(cancellationToken).ConfigureAwait(false);

                var ws = WebSocket!;
                using var receiveToken =
                    detached
                        ? new CancellationTokenSource()
                        : CancellationTokenSource.CreateLinkedTokenSource(CancellationToken, cancellationToken);
                receiveToken.CancelAfter(Options.ReceiveTimeout);
                var memoryPool = MemoryPool;
                var mem = memoryPool.Rent(1 << 10);
                ValueWebSocketReceiveResult resp;
                try
                {
                    try
                    {
                        resp = await ws.ReceiveAsync(mem.Memory, receiveToken.Token).ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        continue;
                    }
                    catch (ObjectDisposedException)
                    {
                        if (IsClosed && !LoopCancellationTokenSource.IsCancellationRequested)
                        {
                            Logger.Debug("Restarting connection");
                            Close("");
                            continue;
                        }
                        else
                        {
                            break;
                        }
                    }
                    catch (WebSocketException e)
                    {
                        receiveToken.Token.ThrowIfCancellationRequested();
                        var logLevel = e.WebSocketErrorCode switch
                        {
                            WebSocketError.InvalidState => LogEventLevel.Debug,
                            WebSocketError.Faulted when Const.IsDebug && ws.State is WebSocketState.Closed or WebSocketState.Aborted =>
                                LogEventLevel.Debug,
                            _ => LogEventLevel.Warning
                        };

                        if (e.InnerException is null)
                        {
                            Logger.Write(logLevel, e, "{Name}", nameof(ws.ReceiveAsync));
                        }
                        else
                        {
                            Logger.Write(logLevel, e, "{Name} {Inner}", nameof(ws.ReceiveAsync),
                                Const.IsDebug ? e.InnerException?.Demystify() : "" ?? "");
                        }

                        continue;
                    }

                    if (resp.Count == 0)
                    {
                        continue;
                    }

                    var data = default(TData);
                    var filter = FilterReceivedMessage(mem.Memory[..resp.Count].Span, resp, ref data);

                    if (filter.HasFlag(ReceiveMessageFilter.ExtractedData))
                    {
                        Data = data;
                    }

                    if (filter.HasFlag(ReceiveMessageFilter.Ignore))
                    {
                        if (filter.HasFlag(ReceiveMessageFilter.ConsumeTheRest))
                        {
                            while (resp is { EndOfMessage: false, Count: > 0 } &&
                                   !receiveToken.IsCancellationRequested &&
                                   !IsClosed)
                            {
                                if (filter.HasFlag(ReceiveMessageFilter.Break))
                                {
                                    break;
                                }

                                resp = await ws.ReceiveAsync(mem.Memory, receiveToken.Token)
                                    .ConfigureAwait(false);
                                if (filter.HasFlag(ReceiveMessageFilter.ContinueFiltering))
                                {
                                    filter = FilterReceivedMessage(mem.Memory[..resp.Count].Span, resp, ref data);
                                }

                                if (filter.HasFlag(ReceiveMessageFilter.ExtractedData))
                                {
                                    Data = data;
                                }
                            }
                        }


                        continue;
                    }

                    var contentLength = resp.Count;

                    if (!resp.EndOfMessage)
                    {
                        var rentSize = Options.AdditionalReceiveBufferSize;
                        IMemoryOwner<byte>? additionalMemory = null;
                        try
                        {
                            while (resp is { EndOfMessage: false, Count: > 0 } &&
                                   !receiveToken.IsCancellationRequested &&
                                   !IsClosed)
                            {
                                if (filter.HasFlag(ReceiveMessageFilter.Break))
                                {
                                    break;
                                }

                                additionalMemory = memoryPool.Rent(Math.Max(rentSize, contentLength * 2));
                                mem.Memory[..contentLength].CopyTo(additionalMemory.Memory);
                                mem.Dispose();
                                (mem, additionalMemory) = (additionalMemory, null);

                                resp = await ws.ReceiveAsync(mem.Memory[contentLength..], receiveToken.Token)
                                    .ConfigureAwait(false);
                                if (filter.HasFlag(ReceiveMessageFilter.ContinueFiltering))
                                {
                                    filter = FilterReceivedMessage(mem.Memory[contentLength..].Span, resp, ref data);
                                }

                                if (!filter.HasFlag(ReceiveMessageFilter.Ignore))
                                {
                                    contentLength += resp.Count;
                                }

                                if (filter.HasFlag(ReceiveMessageFilter.ExtractedData))
                                {
                                    Data = data;
                                }

                                checked
                                {
                                    rentSize *= 2;
                                }
                            }
                        }
                        finally
                        {
                            additionalMemory?.Dispose();
                        }
                    }

                    if (resp.EndOfMessage || filter.HasFlag(ReceiveMessageFilter.ForceDispatch))
                    {
                        await DispatchMessage(data, mem.Memory[..contentLength], CancellationToken)
                            .ConfigureAwait(false);
                    }
                    else if (!IsClosed)
                    {
                        Logger.Warning("Considering websocket in buggy state => closing it");
                        Close("buggy state");
                    }
                }
                finally
                {
                    mem?.Dispose();
                }
            }
        }
        catch (ObjectDisposedException e)
        {
            if (!LoopCancellationTokenSource.IsCancellationRequested)
            {
                throw;
            }

            Logger.Debug(e, "");
        }
    }

    protected abstract Task DispatchMessage(TData? data, ReadOnlyMemory<byte> memory,
        CancellationToken cancellationToken);
}