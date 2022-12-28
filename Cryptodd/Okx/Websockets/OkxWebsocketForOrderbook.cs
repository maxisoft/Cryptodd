using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks.Dataflow;
using Cryptodd.Binance.Json;
using Cryptodd.Http;
using Cryptodd.IoC;
using Cryptodd.Json.Converters;
using Cryptodd.Okx.Json;
using Cryptodd.Okx.Limiters;
using Cryptodd.Okx.Models;
using Cryptodd.Okx.Websockets.Subscriptions;
using Cryptodd.Utils;
using Cryptodd.Websockets;
using Maxisoft.Utils.Objects;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Okx.Websockets;

public abstract class OkxWebsocketForOrderbook<TData, TOptions> : BaseOkxWebsocket<TData, TOptions>
    where TData : PreParsedOkxWebSocketMessage, new() where TOptions : OkxWebsocketForOrderbookOptions, new()
{
    public OkxWebsocketForOrderbook(IOkxLimiterRegistry limiterRegistry, IConfiguration configuration, ILogger logger,
        IClientWebSocketFactory webSocketFactory, Boxed<CancellationToken> cancellationToken) : base(limiterRegistry,
        logger, webSocketFactory, cancellationToken)
    {
        configuration.GetSection("Okx:Orderbook:Websocket").Bind(_options);
        _jsonSerializerOptions = new Lazy<JsonSerializerOptions>(CreateJsonSerializerOptions);
    }

    public const int DefaultOrderbookDepthSize = 400;

    private TOptions _options = new();

    protected override TOptions Options
    {
        get => _options;
        set => _options = value;
    }

    protected ConcurrentBag<ITargetBlock<OkxWebsocketOrderbookResponse>> OrderbooksBufferBlocks { get; set; } = new();

    public void AddBufferBlock(ITargetBlock<OkxWebsocketOrderbookResponse> bufferBlock)
    {
        OrderbooksBufferBlocks.Add(bufferBlock);
    }

    private readonly Lazy<JsonSerializerOptions> _jsonSerializerOptions;

    protected JsonSerializerOptions JsonSerializerOptions => _jsonSerializerOptions.Value;

    internal static JsonSerializerOptions InternalCreateJsonSerializerOptions()
    {
        var res = new JsonSerializerOptions()
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            ReadCommentHandling = JsonCommentHandling.Disallow
        };

        res.Converters.Add(new JsonDoubleConverter());
        res.Converters.Add(new JsonLongConverter());
        res.Converters.Add(new PooledStringJsonConverter(PreParsedOkxWebSocketMessageParser.StringPool));
        res.Converters.Add(new OkxOrderbookEntryJsonConverter());
        res.Converters.Add(new PooledListConverter<OkxOrderbookEntry>()
            { DefaultCapacity = DefaultOrderbookDepthSize });

        return res;
    }

    protected virtual JsonSerializerOptions CreateJsonSerializerOptions() => InternalCreateJsonSerializerOptions();

    public override async ValueTask<bool> ConnectIfNeeded(CancellationToken cancellationToken)
    {
        if (IsClosed)
        {
            try
            {
                using (await SemaphoreSlim.WaitAndGetDisposableAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (IsClosed && ConnectionCounter > 0 && (Subscriptions.PendingSubscriptionsCount > 0 ||
                                                              Subscriptions.SubscriptionsCount > 0))
                    {
                        throw new Exception("unable to reconnect with previously active or pending subscriptions");
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                if (!Disposed.Value)
                {
                    throw;
                }
            }
        }


        return await base.ConnectIfNeeded(cancellationToken).ConfigureAwait(false);
    }

    private static int PriceComparison(OkxOrderbookEntry left, OkxOrderbookEntry right) =>
        left.Price.CompareTo(right.Price);

    protected override async Task DispatchMessage(TData? data, ReadOnlyMemory<byte> memory,
        CancellationToken cancellationToken)
    {
        var unsub = false;
        try
        {
            switch (data?.ArgChannel)
            {
                case "books":
                case "books5":
                case "bbo-tbt":
                case "books-l2-tbt":
                case "books50-l2-tbt":
                    unsub = string.IsNullOrEmpty(data.Event) || data.Event is not "subscribe";
                    switch (data.Action)
                    {
                        case "snapshot":
                        {
                            var ob = JsonSerializer.Deserialize<OkxWebsocketOrderbookResponse>(memory.Span,
                                JsonSerializerOptions);

                            ob?.FirstData.asks.AsSpan().Sort(PriceComparison);
                            ob?.FirstData.bids.AsSpan().Sort(PriceComparison);
                            foreach (var block in OrderbooksBufferBlocks)
                            {
                                await block!.SendAsync(ob, cancellationToken).ConfigureAwait(false);
                            }

                            unsub = true;
                            break;
                        }
                        case "update":
                            unsub = true;
                            break;
                    }

                    break;
            }
        }
        finally
        {
            if (unsub && data is not null)
            {
                await UnSubscribe(cancellationToken,
                    new OkxOrderbookSubscription(data.ArgChannel, data.ArgInstrumentId));
            }
        }


        await base.DispatchMessage(data, memory, cancellationToken);
    }
}

public class
    OkxWebsocketForOrderbook : OkxWebsocketForOrderbook<PreParsedOkxWebSocketMessage, OkxWebsocketForOrderbookOptions>,
        IService
{
    public OkxWebsocketForOrderbook(IOkxLimiterRegistry limiterRegistry, IConfiguration configuration, ILogger logger,
        IClientWebSocketFactory webSocketFactory, Boxed<CancellationToken> cancellationToken) : base(limiterRegistry,
        configuration, logger, webSocketFactory, cancellationToken) { }
}