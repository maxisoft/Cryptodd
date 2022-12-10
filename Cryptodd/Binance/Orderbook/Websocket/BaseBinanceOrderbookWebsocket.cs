using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks.Dataflow;
using Cryptodd.Binance.Models;
using Cryptodd.Binance.Models.Json;
using Cryptodd.Ftx.Models.Json;
using Cryptodd.Http;
using Cryptodd.IoC;
using Cryptodd.Utils;
using Maxisoft.Utils.Empties;
using Maxisoft.Utils.Objects;
using Serilog;

namespace Cryptodd.Binance.Orderbook.Websocket;

public abstract class BaseBinanceOrderbookWebsocket<TOptions> : IDisposable, IAsyncDisposable
    where TOptions : BaseBinanceOrderbookWebsocketOptions, new()
{
    protected IClientWebSocketFactory WebSocketFactory { get; }
    protected ILogger Logger { get; init; }
    protected CancellationTokenSource LoopCancellationTokenSource { get; }
    private readonly Lazy<JsonSerializerOptions> _orderBookJsonSerializerOptions;

    protected JsonSerializerOptions JsonSerializerOptions => _orderBookJsonSerializerOptions.Value;

    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
    private ClientWebSocket? _ws;

    protected ClientWebSocket? WebSocket => _ws;

    private readonly
        ConcurrentQueue<ITargetBlock<ReferenceCounterDisposable<CombinedStreamEnvelope<DepthUpdateMessage>>>>
        _depthTargetBlocks =
            new();

    private readonly ConcurrentDictionary<string, EmptyStruct> _trackedDepthSymbols = new();

    protected IDictionary<string, EmptyStruct> TrackedDepthSymbolsDictionary => _trackedDepthSymbols;
    public ICollection<string> TrackedDepthSymbols => _trackedDepthSymbols.Keys;

    protected virtual MemoryPool<byte> MemoryPool { get; } = MemoryPool<byte>.Shared;

    protected internal TOptions Options { get; set; }

    public long ConnectionCounter { get; protected set; }

    protected BaseBinanceOrderbookWebsocket(ILogger logger, IClientWebSocketFactory webSocketFactory,
        Boxed<CancellationToken> cancellationToken)
    {
        Logger = logger.ForContext(GetType());
        WebSocketFactory = webSocketFactory;
        LoopCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _orderBookJsonSerializerOptions = new Lazy<JsonSerializerOptions>(CreateOrderBookJsonSerializerOptions);
    }

    public virtual void RegisterDepthTargetBlock(
        ITargetBlock<ReferenceCounterDisposable<CombinedStreamEnvelope<DepthUpdateMessage>>> targetBlock)
    {
        _depthTargetBlocks.Enqueue(targetBlock);
    }

    public bool AddDepthSymbol(string symbol)
    {
        if (_trackedDepthSymbols.Count >= Options.MaxStreamCountSoftLimit)
        {
            return false;
        }

        Close();
        return _trackedDepthSymbols.TryAdd(symbol, default);
    }

    public CancellationToken CancellationToken => LoopCancellationTokenSource.Token;

    public virtual void StopReceiveLoop()
    {
        try
        {
            LoopCancellationTokenSource.Cancel();
        }
        catch (ObjectDisposedException e)
        {
            Logger.Verbose(e, "");
        }
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

    protected virtual JsonSerializerOptions CreateOrderBookJsonSerializerOptions()
    {
        var res = new JsonSerializerOptions
            { NumberHandling = JsonNumberHandling.AllowReadingFromString, PropertyNameCaseInsensitive = false };
        res.Converters.Add(new BinancePriceQuantityEntryJsonConverter());
        res.Converters.Add(new PooledListConverter<BinancePriceQuantityEntry<double>>() { DefaultCapacity = 256 });
        res.Converters.Add(new DepthUpdateMessageJsonConverter());
        res.Converters.Add(new CombinedStreamEnvelopeJsonConverter<DepthUpdateMessage>());
        return res;
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

                _ws = await WebSocketFactory
                    .GetWebSocket(CreateUri(), cancellationToken: CancellationToken)
                    .ConfigureAwait(false);
                res = true;
                ConnectionCounter += 1;
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }

        return res;
    }

    protected virtual Uri CreateUri()
    {
        if (!_trackedDepthSymbols.Any())
        {
            throw new ArgumentException("doesn't contains any tracked depth symbol", nameof(_trackedDepthSymbols));
        }

        static void TrimEnd(StringBuilder stringBuilder, char c = '/')
        {
            while (stringBuilder.Length > 0 && stringBuilder[^1] == c)
            {
                stringBuilder.Length -= 1;
            }
        }

        var sb = new StringBuilder(Options.BaseAddress);
        TrimEnd(sb, ' ');
        TrimEnd(sb);


        sb.Append("/stream?streams=");
        foreach (var (symbol, _) in _trackedDepthSymbols)
        {
            sb.Append(symbol.ToLowerInvariant());
            sb.Append("@depth");
            sb.Append('/');
        }

        TrimEnd(sb);

        return new Uri(sb.ToString());
    }

    protected virtual void Close()
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
                Logger.Warning(e, "Error when closing ws");
            }

            Interlocked.CompareExchange(ref _ws, null, ws);
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
                recvToken.CancelAfter(Options.ReceiveTimeout);
                var memoryPool = MemoryPool;
                var mem = memoryPool.Rent(1 << 10);
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
                    catch (ObjectDisposedException)
                    {
                        break;
                    }

                    if (resp.Count == 0)
                    {
                        continue;
                    }


                    if (!PreParsedCombinedStreamEvent.TryParse(mem.Memory[..resp.Count].Span, out var pre))
                    {
                        Logger.Warning("Unable to pre parse message {Message}", pre);
                        Close();
                        continue;
                    }

                    var contentLength = resp.Count;

                    if (!resp.EndOfMessage)
                    {
                        var rentSize = Options.AdditionalReceiveBufferSize;
                        IMemoryOwner<byte>? additionalMemory = null;
                        try
                        {
                            while (!resp.EndOfMessage && resp.Count > 0 && !recvToken.IsCancellationRequested &&
                                   !IsClosed)
                            {
                                additionalMemory = memoryPool.Rent(Math.Max(rentSize, contentLength * 2));
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
                        Logger.Warning("Considering websocket in buggy state => closing it");
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

            Logger.Debug(e, "");
        }
    }

    protected virtual async ValueTask DispatchMessage(PreParsedCombinedStreamEvent pre, ReadOnlyMemory<byte> memory,
        CancellationToken cancellationToken)
    {
        if (pre.Stream.EndsWith("@depth", StringComparison.InvariantCulture) ||
            pre.Stream.EndsWith("@depth@100ms", StringComparison.InvariantCultureIgnoreCase))
        {
            if (_depthTargetBlocks.IsEmpty)
            {
                return;
            }

            var envelope =
                JsonSerializer.Deserialize<CombinedStreamEnvelope<DepthUpdateMessage>>(memory.Span,
                    JsonSerializerOptions);
            var envelopeSafe = new ReferenceCounterDisposable<CombinedStreamEnvelope<DepthUpdateMessage>>(envelope)
                { DisposeOnDeletion = true };

            async ValueTask SendToTargetBlock(
                ITargetBlock<ReferenceCounterDisposable<CombinedStreamEnvelope<DepthUpdateMessage>>> block,
                CancellationToken token)
            {
                var decrement = false;
                try
                {
                    envelopeSafe.Increment();
                    decrement = true;
                    var sent = await block.SendAsync(envelopeSafe, token).ConfigureAwait(false);
                    decrement = false;
                    if (!sent)
                    {
                        envelopeSafe.Decrement();
                    }
                }
                finally
                {
                    if (decrement)
                    {
                        envelopeSafe.Decrement();
                    }
                }
            }

            using (envelopeSafe.NewDecrementOnDispose(increment: true))
            {
                if (_depthTargetBlocks.Count > 1)
                {
                    await Parallel.ForEachAsync(_depthTargetBlocks, cancellationToken, SendToTargetBlock)
                        .ConfigureAwait(false);
                }
                else
                {
                    foreach (var block in _depthTargetBlocks)
                    {
                        await SendToTargetBlock(block, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        LoopCancellationTokenSource.Cancel();
        var ws = _ws;
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
            catch (Exception e) when (e is OperationCanceledException or WebSocketException or ObjectDisposedException)
            {
                Logger.Debug(e, "Error when closing ws");
            }
        }

        Dispose();
        GC.SuppressFinalize(this);
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
        _depthTargetBlocks.Clear();
    }
}