using System.Buffers;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using CryptoDumper.Http;
using CryptoDumper.IoC;
using Maxisoft.Utils.Collections.Queues.Specialized;
using Maxisoft.Utils.Collections.Spans;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace CryptoDumper.Ftx;

internal struct GroupedOrderBookRequest
{
    public string Market { get; set; }
}

public class FtxGroupedOrderBookWebsocket : IService, IDisposable, IAsyncDisposable
{
    internal PooledDeque<GroupedOrderBookRequest> _requests = new PooledDeque<GroupedOrderBookRequest>();
    private readonly IMemoryCache _memoryCache;
    private ClientWebSocket? _ws;
    private readonly SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);
    private readonly IClientWebSocketFactory _webSocketFactory;
    private Stopwatch _pingStopWatch = Stopwatch.StartNew();

    private readonly ILogger _logger;

    public FtxGroupedOrderBookWebsocket(IMemoryCache memoryCache, IClientWebSocketFactory webSocketFactory,
        ILogger logger)
    {
        _memoryCache = memoryCache;
        _webSocketFactory = webSocketFactory;
        _logger = logger;
    }

    internal CancellationTokenSource LoopCancellationTokenSource = new CancellationTokenSource();
    public CancellationToken CancellationToken => LoopCancellationTokenSource.Token;

    public bool IsClosed
    {
        get
        {
            var ws = _ws;
            return ws is null ||
                   (!ws.State.HasFlag(WebSocketState.Open) && !ws.State.HasFlag(WebSocketState.Connecting));
        }
    }

    internal async ValueTask ProcessRequests()
    {
        while (!LoopCancellationTokenSource.IsCancellationRequested)
        {
            await ConnectIfNeeded().ConfigureAwait(false);
            await PingRemote().ConfigureAwait(false);
            if (_requests.IsEmpty) return;

            while (_requests.TryPopFront(out var request))
            {
                await _ws!.SendAsync(
                    Encoding.UTF8.GetBytes(
                        "{\"op\": \"subscribe\", \"channel\": \"orderbook\", \"market\": \"" +
                        $"{request.Market}" +
                        "\"}"),
                    WebSocketMessageType.Text, true, CancellationToken);
            }
        }
    }

    internal async ValueTask RecvLoop()
    {
        try
        {
            while (!LoopCancellationTokenSource.IsCancellationRequested)
            {
                await ConnectIfNeeded().ConfigureAwait(false);

                var ws = _ws!;
                using var recvToken = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
                recvToken.CancelAfter(60_000);
                IMemoryOwner<byte>? mem = MemoryPool<byte>.Shared.Rent(1 << 10);
                ValueWebSocketReceiveResult resp;
                try
                {
                    try
                    {
                        resp = await ws.ReceiveAsync(mem.Memory, recvToken.Token).ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        continue;
                    }

                    if (resp.Count == 0)
                    {
                        continue;
                    }


                    if (!PreParsedFtxWsMessage.TryParse(mem.Memory[..resp.Count].Span, out var pre))
                    {
                        _logger.Warning("Unable to pre parse message {0}", pre);
                        Close();
                        continue;
                    }

                    var contentLength = resp.Count;

                    if (!resp.EndOfMessage)
                    {
                        var rentSize = 128 << 10;
                        IMemoryOwner<byte>? additionalMemory = null;
                        try
                        {
                            while (!resp.EndOfMessage && resp.Count > 0 && !recvToken.IsCancellationRequested &&
                                   !IsClosed)
                            {
                                additionalMemory = MemoryPool<byte>.Shared.Rent(Math.Max(rentSize, contentLength * 2));
                                mem.Memory[..contentLength].CopyTo(additionalMemory.Memory);
                                mem.Dispose();
                                mem = additionalMemory;
                                additionalMemory = null;


                                resp = await ws.ReceiveAsync(mem.Memory[contentLength..], recvToken.Token)
                                    .ConfigureAwait(false);
                                rentSize <<= 1;
                                contentLength += resp.Count;
                            }
                        }
                        finally
                        {
                            additionalMemory?.Dispose();
                        }
                    }

                    if (resp.EndOfMessage)
                    {
                        await DispatchMessage(pre, mem.Memory[..contentLength], CancellationToken)
                            .ConfigureAwait(false);
                    }
                    else if (!IsClosed)
                    {
                        _logger.Warning("Considering websocket in buggy state => closing it");
                        Close();
                        continue;
                    }
                    else
                    {
                        continue;
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

            _logger.Debug(e, "");
        }
    }

    private ValueTask DispatchMessage(PreParsedFtxWsMessage pre, Memory<byte> memory,
        CancellationToken cancellationToken)
    {
        switch (pre.Channel)
        {
            default:
                _logger.Warning("No handling for {0}", pre);
                break;
        }
        return ValueTask.CompletedTask;
    }

    private Task PingRemote()
    {
        var pingTask = Task.CompletedTask;
        if (_pingStopWatch.ElapsedMilliseconds > 15_000 && !IsClosed)
        {
            pingTask = _ws?.SendAsync(Encoding.UTF8.GetBytes("{\"op\": \"ping\"}"), WebSocketMessageType.Text, true,
                CancellationToken) ?? Task.CompletedTask;
            pingTask = pingTask.ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    _pingStopWatch.Restart();
                }
            }, CancellationToken);
        }

        return pingTask;
    }

    private async ValueTask<bool> ConnectIfNeeded()
    {
        var res = false;
        if (IsClosed)
        {
            await _semaphoreSlim.WaitAsync(CancellationToken).ConfigureAwait(false);
            try
            {
                if (!IsClosed)
                {
                    return false;
                }

                Close();

                _ws = await _webSocketFactory.GetWebSocket(new Uri("wss://ftx.com/ws/"), CancellationToken)
                    .ConfigureAwait(false);
                _pingStopWatch = Stopwatch.StartNew();
                res = true;
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }

        return res;
    }

    internal void Close()
    {
        var ws = _ws;
        if (ws is { })
        {
            try
            {
                ws.Abort();
                ws.Dispose();
            }
            catch (ObjectDisposedException e)
            {
                _logger.Warning(e, "Error when closing ws");
            }

            Interlocked.CompareExchange(ref _ws, null, ws);
        }
    }

    public async ValueTask DisposeAsync()
    {
        var ws = _ws;
        if (ws is { State: WebSocketState.Open })
        {
            using var closeCancellationToken = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
            closeCancellationToken.CancelAfter(1000);
            try
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, closeCancellationToken.Token);
            }
            catch (OperationCanceledException e)
            {
                _logger.Warning(e, "Error when closing ws");
            }
        }

        Dispose();
    }

    public void Dispose()
    {
        Close();
        LoopCancellationTokenSource.Cancel();
        _semaphoreSlim.Dispose();
        LoopCancellationTokenSource.Dispose();
    }
}