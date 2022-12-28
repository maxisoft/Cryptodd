using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks.Dataflow;
using Cryptodd.Binance.Json;
using Cryptodd.Binance.Models;
using Cryptodd.Binance.Models.Json;
using Cryptodd.Ftx.Models.Json;
using Cryptodd.Http;
using Cryptodd.Json;
using Cryptodd.Json.Converters;
using Cryptodd.Utils;
using Cryptodd.Websockets;
using Maxisoft.Utils.Empties;
using Maxisoft.Utils.Objects;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

namespace Cryptodd.Binance.Orderbooks.Websockets;

public abstract class BaseBinanceOrderbookWebsocket<TOptions> : BaseWebsocket<PreParsedCombinedStreamEvent, TOptions>, IDisposable,
    IAsyncDisposable
    where TOptions : BaseBinanceOrderbookWebsocketOptions, new()
{
    private readonly
        ConcurrentQueue<ITargetBlock<ReferenceCounterDisposable<CombinedStreamEnvelope<DepthUpdateMessage>>>>
        _depthTargetBlocks =
            new();

    private readonly Lazy<JsonSerializerOptions> _orderBookJsonSerializerOptions;

    private readonly HashSet<string> _symbolBlackList = new();

    protected readonly ConcurrentDictionary<string, EmptyStruct> _trackedDepthSymbols = new();

    protected BaseBinanceOrderbookWebsocket(ILogger logger, IClientWebSocketFactory webSocketFactory,
        Boxed<CancellationToken> cancellationToken) : base(logger, webSocketFactory, cancellationToken)
    {
        Logger = logger.ForContext(GetType());
        WebSocketFactory = webSocketFactory;
        LoopCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _orderBookJsonSerializerOptions = new Lazy<JsonSerializerOptions>(CreateOrderBookJsonSerializerOptions);
    }

    public BinanceWebsocketStats DepthWebsocketStats { get; protected set; } = new();
    protected IClientWebSocketFactory WebSocketFactory { get; }
    protected ILogger Logger { get; init; }
    protected override ClientWebSocket? WebSocket { get; set; }
    protected CancellationTokenSource LoopCancellationTokenSource { get; }

    protected JsonSerializerOptions JsonSerializerOptions => _orderBookJsonSerializerOptions.Value;

    protected IDictionary<string, EmptyStruct> TrackedDepthSymbolsDictionary => _trackedDepthSymbols;
    public ICollection<string> TrackedDepthSymbols => _trackedDepthSymbols.Keys;

    protected override TOptions Options { get; set; } = new ();

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

        if (_trackedDepthSymbols.ContainsKey(symbol))
        {
            return false;
        }
        Close("adding new symbol");
        return _trackedDepthSymbols.TryAdd(symbol, default);
    }

    protected virtual JsonSerializerOptions CreateOrderBookJsonSerializerOptions()
    {
        var res = new JsonSerializerOptions
            { NumberHandling = JsonNumberHandling.AllowReadingFromString, PropertyNameCaseInsensitive = false };
        res.Converters.Add(new BinancePriceQuantityEntryJsonConverter());
        res.Converters.Add(new PooledListConverter<BinancePriceQuantityEntry<double>> { DefaultCapacity = 256 });
        res.Converters.Add(new DepthUpdateMessageJsonConverter());
        res.Converters.Add(new CombinedStreamEnvelopeJsonConverter<DepthUpdateMessage>());
        return res;
    }

    protected override Uri CreateUri()
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
            if (IsBlacklistedSymbol(symbol))
            {
                continue;
            }

            sb.Append(symbol.ToLowerInvariant());
            sb.Append("@depth");
            sb.Append('/');
        }

        TrimEnd(sb);

        return new Uri(sb.ToString());
    }

    public bool IsBlacklistedSymbol(string symbol) =>
        // ReSharper disable once InconsistentlySynchronizedField
        _symbolBlackList.Contains(symbol);

    public bool BlacklistSymbol(string symbol)
    {
        lock (_symbolBlackList)
        {
            return _symbolBlackList.Add(symbol);
        }
    }

    protected override bool Dispose(bool disposing)
    {
        disposing = base.Dispose(disposing);
        _depthTargetBlocks.Clear();
        return disposing;
    }

    protected override ReceiveMessageFilter FilterReceivedMessage(Span<byte> message,
        ValueWebSocketReceiveResult receiveResult,
        ref PreParsedCombinedStreamEvent data)
    {
        ReceiveMessageFilter res;
        if (PreParsedCombinedStreamEvent.TryParse(message, out data))
        {
            res = ReceiveMessageFilter.ExtractedData;
        }
        else
        {
            res = ReceiveMessageFilter.Ignore | ReceiveMessageFilter.ConsumeTheRest;
        }

        return res;
    }

    protected override async Task DispatchMessage(PreParsedCombinedStreamEvent data, ReadOnlyMemory<byte> memory,
        CancellationToken cancellationToken)
    {
        if (!BinanceStreamNameHelper.IsDepth(data.Stream))
        {
            Logger.Warning("Got unhandled stream {Stream}", data.Stream);
            return;
        }

        DepthWebsocketStats.RegisterTick();
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

        using (envelopeSafe.NewDecrementOnDispose(true))
        {
            if (IsBlacklistedSymbol(envelope.Data.Symbol))
            {
                return;
            }

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

        DepthWebsocketStats.RegisterSymbol(envelope.Data.Symbol);
    }
}