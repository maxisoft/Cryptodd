using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks.Dataflow;
using CryptoDumper.Ftx.Models;
using CryptoDumper.Ftx.Models.Json;
using CryptoDumper.Http;
using CryptoDumper.IoC;
using Maxisoft.Utils.Collections.Queues.Specialized;
using Maxisoft.Utils.Collections.Spans;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace CryptoDumper.Ftx;

public readonly record struct GroupedOrderBookRequest(string Market, double Grouping)
{
}

public class FtxGroupedOrderBookWebsocket : IService, IDisposable, IAsyncDisposable
{
    internal BufferBlock<GroupedOrderBookRequest> _requests = new BufferBlock<GroupedOrderBookRequest>();
    private readonly IMemoryCache _memoryCache;
    private ClientWebSocket? _ws;
    private readonly SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);
    private readonly IClientWebSocketFactory _webSocketFactory;
    private Stopwatch _pingStopWatch = Stopwatch.StartNew();

    private List<ITargetBlock<GroupedOrderbookDetails>> _targetBlocks = new ();

    private readonly ILogger _logger;

    public FtxGroupedOrderBookWebsocket(IMemoryCache memoryCache, IClientWebSocketFactory webSocketFactory,
        ILogger logger)
    {
        _memoryCache = memoryCache;
        _webSocketFactory = webSocketFactory;
        _logger = logger;
    }

    internal readonly CancellationTokenSource LoopCancellationTokenSource = new CancellationTokenSource();

    private static readonly JsonSerializerOptions OrderBookJsonSerializerOptions = CreateOrderBookJsonSerializerOptions();

    private static JsonSerializerOptions CreateOrderBookJsonSerializerOptions() 
    {
        var res = new JsonSerializerOptions()
            { NumberHandling = JsonNumberHandling.AllowReadingFromString, PropertyNameCaseInsensitive = true };
        res.Converters.Add(new PriceSizePairConverter());
        return res;
    }
        

    public CancellationToken CancellationToken => LoopCancellationTokenSource.Token;

    public int NumRemainingRequests()
    {
        return _requests.Count;
    }

    public void RegisterTargetBlock(ITargetBlock<GroupedOrderbookDetails> block)
    {
        _targetBlocks.Add(block);
    }

    public bool RegisterGroupedOrderBookRequest(string market, double grouping)
    {
        return _requests.Post(new GroupedOrderBookRequest(market, grouping));
    }

    public bool IsClosed
    {
        get
        {
            var ws = _ws;
            return ws is null ||
                   (!ws.State.HasFlag(WebSocketState.Open) && !ws.State.HasFlag(WebSocketState.Connecting));
        }
    }

    internal async ValueTask ProcessRequests(CancellationToken cancellationToken)
    {
        while (!LoopCancellationTokenSource.IsCancellationRequested)
        {
            await ConnectIfNeeded().ConfigureAwait(false);
            await PingRemote().ConfigureAwait(false);
            if (_requests.Count == 0) return;
            while (_requests.TryReceive(out var request) && !cancellationToken.IsCancellationRequested)
            {
                await _ws!.SendAsync(
                    Encoding.UTF8.GetBytes(
                        "{\"op\": \"subscribe\", \"channel\": \"orderbookGrouped\", \"market\": \"" +
                        $"{request.Market}" + "\", \"grouping\": " + request.Grouping.ToString(CultureInfo.InvariantCulture) + 
                        "}"),
                    WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    internal async Task RecvLoop()
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
                        _logger.Warning("Unable to pre parse message {Message}", pre);
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

    private async ValueTask HandleOrderbookGrouped(PreParsedFtxWsMessage pre, Memory<byte> memory, CancellationToken cancellationToken)
    {
        Debug.Assert(pre.Channel == "orderbookGrouped");
        if (pre.Type is "subscribed" or "unsubscribed") return;
        
        var unsub = Unsubscribe(pre);
        try
        {
            if (pre.Type != "partial")
            {
                return;
            }
            
            var orderbookGroupedWrapper = JsonSerializer.Deserialize<GroupedOrderbookDetails>(memory.Span,
                OrderBookJsonSerializerOptions);
            if (orderbookGroupedWrapper is null) return;
            foreach (var block in _targetBlocks)
            {
                await block.SendAsync(orderbookGroupedWrapper, cancellationToken);
            }
        }
        finally
        {
            if (!unsub.IsCompleted)
            {
                await unsub;
            }
        }
    }

    private async Task Unsubscribe(PreParsedFtxWsMessage pre)
    {
        StringBuilder stringBuilder = new StringBuilder();
        stringBuilder.Append("{\"op\":\"unsubscribe\"");
        if (!string.IsNullOrWhiteSpace(pre.Channel))
        {
            stringBuilder.Append(CultureInfo.InvariantCulture, $",\"channel\":\"{pre.Channel}\"");
        }

        if (!string.IsNullOrWhiteSpace(pre.Market))
        {
            stringBuilder.Append(CultureInfo.InvariantCulture, $",\"market\":\"{pre.Market}\"");
        }

        if (pre.Grouping.HasValue)
        {
            stringBuilder.Append(CultureInfo.InvariantCulture, $",\"grouping\":\"{pre.Grouping}\"");
        }
        stringBuilder.Append('}');
        await (_ws?.SendAsync(Encoding.UTF8.GetBytes(stringBuilder.ToString()), WebSocketMessageType.Text, true,
            CancellationToken) ?? Task.CompletedTask);
    }
    
    private ValueTask DispatchMessage(PreParsedFtxWsMessage pre, Memory<byte> memory,
        CancellationToken cancellationToken)
    {
        switch (pre.Type, pre.Channel)
        {
            case ("subscribed" or "unsubscribed" or "pong", _):
                break;
            case (_, "orderbookGrouped"):
                return HandleOrderbookGrouped(pre, memory, cancellationToken);
            default:
                _logger.Warning("No handling for {Pre}", pre);
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
        LoopCancellationTokenSource.Cancel();
        var ws = _ws;
        if (ws is { State: WebSocketState.Open })
        {
            using var closeCancellationToken = new CancellationTokenSource();
            closeCancellationToken.CancelAfter(1000);
            try
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, closeCancellationToken.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or WebSocketException)
            {
                if (_requests.Count > 0)
                {
                    _logger.Debug(e, "Error when closing ws");
                }
            }
        }

        Dispose();
    }

    public void Dispose()
    {
        Close();
        try
        {
            if (!LoopCancellationTokenSource.IsCancellationRequested)
            {
                LoopCancellationTokenSource.Cancel();
            }
        }
        catch (ObjectDisposedException)
        {
        }
        
        _semaphoreSlim.Dispose();
        LoopCancellationTokenSource.Dispose();
    }
}