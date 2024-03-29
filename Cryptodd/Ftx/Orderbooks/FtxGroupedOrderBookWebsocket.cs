﻿using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks.Dataflow;
using Cryptodd.Ftx.Models;
using Cryptodd.Ftx.Models.Json;
using Cryptodd.Http;
using Cryptodd.IoC;
using Maxisoft.Utils.Objects;
using Serilog;

namespace Cryptodd.Ftx.Orderbooks;

public readonly record struct GroupedOrderBookRequest(string Market, double Grouping) { }

public class FtxGroupedOrderBookWebsocket : IService, IDisposable, IAsyncDisposable
{
    private const string WebsocketUrl = "wss://ftx.com/ws/";

    internal static readonly JsonSerializerOptions OrderBookJsonSerializerOptions =
        CreateOrderBookJsonSerializerOptions();

    private readonly ILogger _logger;
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);

    private readonly List<ITargetBlock<GroupedOrderbookDetails>> _targetBlocks = new();
    private readonly IClientWebSocketFactory _webSocketFactory;

    internal readonly CancellationTokenSource LoopCancellationTokenSource;
    private Stopwatch _pingStopWatch = Stopwatch.StartNew();
    private ClientWebSocket? _ws;
    internal BufferBlock<GroupedOrderBookRequest> Requests = new();

    public FtxGroupedOrderBookWebsocket(IClientWebSocketFactory webSocketFactory,
        ILogger logger, Boxed<CancellationToken> cancellationToken)
    {
        _webSocketFactory = webSocketFactory;
        _logger = logger;
        LoopCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
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
                if (Requests.Count > 0)
                {
                    _logger.Debug(e, "Error when closing ws");
                }
            }
        }

        Dispose();
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
    }

    private static JsonSerializerOptions CreateOrderBookJsonSerializerOptions()
    {
        var res = new JsonSerializerOptions
            { NumberHandling = JsonNumberHandling.AllowReadingFromString, PropertyNameCaseInsensitive = true };
        res.Converters.Add(new PriceSizePairConverter());
        res.Converters.Add(new PooledListPriceSizePairConverter());
        return res;
    }

    public int NumRemainingRequests() => Requests.Count;

    public void RegisterTargetBlock(ITargetBlock<GroupedOrderbookDetails> block)
    {
        _targetBlocks.Add(block);
    }

    public bool RegisterGroupedOrderBookRequest(string market, double grouping) =>
        Requests.Post(new GroupedOrderBookRequest(market, grouping));

    internal async ValueTask ProcessRequests(CancellationToken cancellationToken)
    {
        while (!LoopCancellationTokenSource.IsCancellationRequested)
        {
            await ConnectIfNeeded().ConfigureAwait(false);
            await PingRemote().ConfigureAwait(false);
            if (Requests.Count == 0)
            {
                return;
            }

            while (Requests.TryReceive(out var request) && !cancellationToken.IsCancellationRequested)
            {
                await _ws!.SendAsync(
                    Encoding.UTF8.GetBytes(
                        "{\"op\":\"subscribe\",\"channel\":\"orderbookGrouped\",\"market\":\"" +
                        $"{request.Market}" + "\",\"grouping\":" +
                        request.Grouping.ToString(CultureInfo.InvariantCulture) +
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

    private async ValueTask HandleOrderbookGrouped(PreParsedFtxWsMessage pre, Memory<byte> memory,
        CancellationToken cancellationToken)
    {
        Debug.Assert(pre.Channel == "orderbookGrouped");
        if (pre.Type is "subscribed" or "unsubscribed")
        {
            return;
        }

        var unsub = Unsubscribe(pre);
        try
        {
            if (pre.Type != "partial")
            {
                return;
            }

            var orderbookGroupedWrapper = JsonSerializer.Deserialize<GroupedOrderbookDetails>(memory.Span,
                OrderBookJsonSerializerOptions);
            if (orderbookGroupedWrapper is null)
            {
                return;
            }

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
        var stringBuilder = new StringBuilder();
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
            pingTask = _ws?.SendAsync(Encoding.UTF8.GetBytes("{\"op\":\"ping\"}"), WebSocketMessageType.Text, true,
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

                _ws = await _webSocketFactory
                    .GetWebSocket(new Uri(WebsocketUrl), cancellationToken: CancellationToken)
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
}