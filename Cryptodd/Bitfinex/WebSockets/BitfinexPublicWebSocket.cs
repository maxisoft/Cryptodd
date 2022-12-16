using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks.Dataflow;
using Cryptodd.Bitfinex.Models;
using Cryptodd.Bitfinex.Models.Json;
using Cryptodd.Ftx.Models.Json;
using Cryptodd.Http;
using Cryptodd.IoC;
using Cryptodd.Pairs;
using Maxisoft.Utils.Objects;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Bitfinex.WebSockets;

public readonly record struct GroupedOrderBookRequest(string Symbol, string Precision = "P0",
    int Length = GroupedOrderBookRequest.DefaultOrderBookLength)
{
    internal const int DefaultOrderBookLength = 25;
}

public class BitfinexPublicWebSocketOptions
{
    public const string DefaultUrl = "wss://api-pub.bitfinex.com/ws/2";
    public string WebsocketUrl { get; set; } = DefaultUrl;
    public int MaxChannel { get; set; } = 25;
}

public class BitfinexPublicWebSocket : IService, IDisposable, IAsyncDisposable
{
    private readonly BitfinexPublicWebSocketOptions _options = new();

    internal static readonly JsonSerializerOptions OrderBookJsonSerializerOptions =
        CreateOrderBookJsonSerializerOptions();

    private readonly ConcurrentDictionary<long, string> _channelToSymbol = new();

    private readonly ILogger _logger;
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
    private readonly SemaphoreSlim _subscribedSemaphore;
    public int SubscriptionsCount => MaxChannel - _subscribedSemaphore.CurrentCount;

    public int MaxChannel => _options.MaxChannel;

    public int ActiveAndPendingRequestCount => SubscriptionsCount + _requests.Count;

    public int RequestSlotAvailable => Math.Max(MaxChannel - ActiveAndPendingRequestCount, 0);

    private readonly List<ITargetBlock<OrderbookEnvelope>> _targetBlocks = new();
    private readonly IClientWebSocketFactory _webSocketFactory;

    internal readonly CancellationTokenSource LoopCancellationTokenSource;
    private Stopwatch _pingStopWatch = Stopwatch.StartNew();
    internal ClientWebSocket? _ws;

    private long _messageCounter;
    private readonly BufferBlock<GroupedOrderBookRequest> _requests;

    public BitfinexPublicWebSocket(IClientWebSocketFactory webSocketFactory, IConfiguration configuration,
        ILogger logger, Boxed<CancellationToken> cancellationToken)
    {
        configuration.GetSection("Bitfinex:Websocket").Bind(_options);
        _webSocketFactory = webSocketFactory;
        _logger = logger;
        LoopCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _subscribedSemaphore = new SemaphoreSlim(MaxChannel, MaxChannel);
        _requests = new BufferBlock<GroupedOrderBookRequest>(new DataflowBlockOptions()
            { BoundedCapacity = Math.Max(1024, MaxChannel) });
    }


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

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        try
        {
            LoopCancellationTokenSource.Cancel();
        }
        catch (ObjectDisposedException) { }

        var ws = _ws;
        if (ws is { State: WebSocketState.Open })
        {
            await CloseWebSocketAsync(ws);
        }

        Dispose();
    }

    protected virtual async ValueTask CloseWebSocketAsync(ClientWebSocket ws)
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

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        Close();
        try
        {
            if (!LoopCancellationTokenSource.IsCancellationRequested)
            {
                LoopCancellationTokenSource.Cancel();
            }
        }
        catch (ObjectDisposedException e) // lgtm [cs/empty-catch-block]
        {
            Debug.Write(e);
        }

        _semaphoreSlim.Dispose();
        LoopCancellationTokenSource.Dispose();
        _subscribedSemaphore.Dispose();
    }

    private static JsonSerializerOptions CreateOrderBookJsonSerializerOptions()
    {
        var res = new JsonSerializerOptions
            { NumberHandling = JsonNumberHandling.AllowReadingFromString, PropertyNameCaseInsensitive = true };
        res.Converters.Add(new PooledListConverter<PriceCountSizeTuple>());
        res.Converters.Add(new PriceCountSizeConverter());
        res.Converters.Add(new OrderbookEnvelopeConverter());
        return res;
    }

    public int NumRemainingRequests => _requests.Count;

    public void RegisterTargetBlock(ITargetBlock<OrderbookEnvelope> block)
    {
        _targetBlocks.Add(block);
    }

    public bool RegisterGroupedOrderBookRequest(string market, int precision,
        int length = GroupedOrderBookRequest.DefaultOrderBookLength) =>
        _requests.Post(new GroupedOrderBookRequest($"t{market}", $"P{precision}", length));

    internal async ValueTask ProcessRequests(CancellationToken cancellationToken)
    {
        while (!LoopCancellationTokenSource.IsCancellationRequested)
        {
            await ConnectIfNeeded().ConfigureAwait(false);
            await PingRemote().ConfigureAwait(false);
            if (_requests.Count == 0)
            {
                return;
            }

            while (_requests.TryReceive(out var request) && !cancellationToken.IsCancellationRequested)
            {
                await _subscribedSemaphore.WaitAsync(cancellationToken);
                try
                {
                    var subId = Interlocked.Increment(ref _messageCounter);
                    subId <<= 32;
                    subId |= (uint)(HashCode.Combine(PairHasher.Hash(request.Symbol), request) & int.MaxValue);
                    await _ws!.SendAsync(
                        Encoding.UTF8.GetBytes(
                            $"{{\"event\":\"subscribe\",\"channel\":\"book\",\"symbol\":\"{request.Symbol}\",\"freq\":\"f1\",\"prec\":\"{request.Precision}\",\"subId\":\"{subId}\",\"len\":\"{request.Length}\"}}"),
                        WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    _subscribedSemaphore.Release();
                    throw;
                }
            }
        }
    }

    private long _cid = 1;

    public async ValueTask<bool> Ping()
    {
        var ws = _ws;
        if (ws is { State: WebSocketState.Open })
        {
            var buff = Encoding.UTF8.GetBytes(@"{""event"":""ping"", ""cid"":" + Interlocked.Increment(ref _cid) + '}');
            await ws.SendAsync(buff, WebSocketMessageType.Text, true,
                CancellationToken);
            return true;
        }

        return false;
    }

    internal async Task RecvLoop(CancellationToken loopCancellationToken)
    {
        try
        {
            while (!LoopCancellationTokenSource.IsCancellationRequested &&
                   !loopCancellationToken.IsCancellationRequested)
            {
                await ConnectIfNeeded().ConfigureAwait(false);

                var ws = _ws!;
                using var recvToken = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
                recvToken.CancelAfter(60_000);
                var mem = MemoryPool<byte>.Shared.Rent(1 << 10);
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
                    catch (OperationCanceledException)
                    {
                        continue;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }

                    if (resp.Count == 0)
                    {
                        continue;
                    }


                    if (!PreParsedBitfinexWsMessage.TryParse(mem.Memory[..resp.Count].Span, out var pre))
                    {
                        _logger.Warning("Unable to pre parse message {Message}",
                            Encoding.UTF8.GetString(mem.Memory[..resp.Count].Span));
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
                    }
                }
                catch (ObjectDisposedException e)
                {
                    try
                    {
                        if (!CancellationToken.IsCancellationRequested)
                        {
                            _logger.Warning(e, "");
                        }
                    }
                    catch (ObjectDisposedException e2)
                    {
                        _logger.Verbose(e2, "");
                    }

                    return;
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

    private async ValueTask HandleOrderbookGrouped(PreParsedBitfinexWsMessage pre, Memory<byte> memory,
        CancellationToken cancellationToken)
    {
        Debug.Assert(pre.ChanId > 0);

        var unsub = Unsubscribe(pre);
        try
        {
            var orderbookEnvelope = JsonSerializer.Deserialize<OrderbookEnvelope>(memory.Span,
                OrderBookJsonSerializerOptions);
            if (orderbookEnvelope is null)
            {
                return;
            }

            if (_channelToSymbol.TryGetValue(orderbookEnvelope.Channel, out var symbol) &&
                !string.IsNullOrWhiteSpace(symbol))
            {
                orderbookEnvelope.Symbol = symbol;
            }

            if (orderbookEnvelope.Orderbook.Count is not (2 or 50 or 200 or 500))
            {
                return;
            }

            foreach (var block in _targetBlocks)
            {
                await block.SendAsync(orderbookEnvelope, cancellationToken);
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

    private async Task Unsubscribe(PreParsedBitfinexWsMessage pre)
    {
        var stringBuilder = new StringBuilder();
        stringBuilder.Append("{\"event\":\"unsubscribe\"");
        if (pre.ChanId > 0)
        {
            stringBuilder.Append(CultureInfo.InvariantCulture, $",\"chanId\":{pre.ChanId}");
        }

        stringBuilder.Append('}');
        await (_ws?.SendAsync(Encoding.UTF8.GetBytes(stringBuilder.ToString()), WebSocketMessageType.Text, true,
            CancellationToken) ?? Task.CompletedTask);
    }

    private ValueTask DispatchMessage(PreParsedBitfinexWsMessage pre, Memory<byte> memory,
        CancellationToken cancellationToken)
    {
        if (!pre.IsArray)
        {
            switch (pre.Event, pre.Channel)
            {
                case ("subscribed", _):
                    _channelToSymbol[pre.ChanId] = pre.Symbol;
                    break;
                case ("unsubscribed", _):
                    _subscribedSemaphore.Release();
                    break;
                case ("pong" or "info" or "conf", _):
                    break;
                default:
                    _logger.Warning("No handling for {Pre} {Message}", pre, Encoding.UTF8.GetString(memory.Span));
                    break;
            }
        }

        if (pre.IsHearthBeat)
        {
            return ValueTask.CompletedTask;
        }

        return pre.IsArray ? HandleOrderbookGrouped(pre, memory, cancellationToken) : ValueTask.CompletedTask;
    }

    private Task PingRemote()
    {
        var pingTask = Task.CompletedTask;
        if (_pingStopWatch.ElapsedMilliseconds > 15_000 && !IsClosed)
        {
            pingTask = _ws?.SendAsync(Encoding.UTF8.GetBytes("{\"event\":\"ping\"}"), WebSocketMessageType.Text, true,
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

                _ws = await CreateWebSocket()
                    .ConfigureAwait(false);
                _pingStopWatch = Stopwatch.StartNew();
                res = true;
            }
            finally
            {
                _semaphoreSlim.Release();
            }

            var flags = (long)RemoteBitfinexWebsocketConfigFlags.Timestamp ^
                        (long)RemoteBitfinexWebsocketConfigFlags.SeqAll ^
                        (long)RemoteBitfinexWebsocketConfigFlags.BulkUpdates;
            var payload = Encoding.UTF8.GetBytes("{\"event\":\"conf\",\"flags\":" + flags + " }");
            await _ws.SendAsync(payload,
                WebSocketMessageType.Text,
                true,
                CancellationToken);
        }

        return res;
    }

    protected virtual ValueTask<ClientWebSocket> CreateWebSocket() =>
        _webSocketFactory
            .GetWebSocket(new Uri(_options.WebsocketUrl), cancellationToken: CancellationToken);

    protected internal virtual void Close()
    {
        var ws = _ws;
        if (ws is not null)
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
}