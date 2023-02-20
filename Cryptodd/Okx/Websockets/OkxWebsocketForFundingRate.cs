using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks.Dataflow;
using Cryptodd.Binance.Json;
using Cryptodd.Http;
using Cryptodd.IoC;
using Cryptodd.Json;
using Cryptodd.Json.Converters;
using Cryptodd.Okx.Json;
using Cryptodd.Okx.Limiters;
using Cryptodd.Okx.Models;
using Cryptodd.Okx.Models.HttpResponse;
using Cryptodd.Okx.Websockets.Subscriptions;
using Cryptodd.Utils;
using Cryptodd.Websockets;
using Maxisoft.Utils.Collections.Lists;
using Maxisoft.Utils.Empties;
using Maxisoft.Utils.Objects;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Okx.Websockets;

public class OkxWebsocketForFundingRateOptions : BaseOkxWebsocketOptions
{
    public OkxWebsocketForFundingRateOptions()
    {
        MaxStreamCountSoftLimit = 32;
    }
}

public abstract class OkxWebsocketForFundingRate<TData, TOptions> : BaseOkxWebsocket<TData, TOptions>
    where TData : PreParsedOkxWebSocketMessage, new() where TOptions : OkxWebsocketForFundingRateOptions, new()
{
    public const string ChannelName = OkxFundingRateSubscription.DefaultChannel;

    public OkxWebsocketForFundingRate(IOkxLimiterRegistry limiterRegistry, IConfiguration configuration, ILogger logger,
        IClientWebSocketFactory webSocketFactory, Boxed<CancellationToken> cancellationToken) : base(limiterRegistry,
        logger, webSocketFactory, cancellationToken)
    {
        configuration.GetSection("Okx:FundingRate:Websocket").Bind(_options);
        _jsonSerializerOptions = new Lazy<JsonSerializerOptions>(CreateJsonSerializerOptions);
    }


    private TOptions _options = new();

    protected override TOptions Options
    {
        get => _options;
        set => _options = value;
    }

    protected ConcurrentBag<ITargetBlock<OkxWebsocketFundingRateResponse>> BufferBlocks { get; set; } = new();

    public void AddBufferBlock(ITargetBlock<OkxWebsocketFundingRateResponse> bufferBlock)
    {
        BufferBlocks.Add(bufferBlock);
    }

    private readonly Lazy<JsonSerializerOptions> _jsonSerializerOptions;

    protected JsonSerializerOptions JsonSerializerOptions => _jsonSerializerOptions.Value;

    protected ConcurrentDictionary<OkxSubscription, EmptyStruct> ExpectedSubscriptions { get; } = new();

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
        res.Converters.Add(new OkxHttpFundingRateJsonConverter());
        res.Converters.Add(new SafeJsonDoubleConverter<SafeJsonDoubleDefaultValue>());
        res.Converters.Add(new OneItemListJsonConverter<OkxHttpFundingRate>());

        return res;
    }

    protected virtual JsonSerializerOptions CreateJsonSerializerOptions() => InternalCreateJsonSerializerOptions();

    private async Task Resubscribe(CancellationToken cancellationToken)
    {
        ArrayList<OkxSubscription> subscriptions = new();
        var limit = Options.MaxStreamCountSoftLimit;
        foreach (var (subscription, _) in ExpectedSubscriptions)
        {
            if (Subscriptions.GetState(in subscription) != OkxSubscriptionSate.None)
            {
                continue;
            }

            subscriptions.Add(subscription);
            if (subscriptions.Count >= limit)
            {
                break;
            }
        }

        if (subscriptions.Count > 0)
        {
            await MultiSubscribe(subscriptions, cancellationToken).ConfigureAwait(false);
        }
    }

    public override async ValueTask<bool> ConnectIfNeeded(CancellationToken cancellationToken)
    {
        var res = await base.ConnectIfNeeded(cancellationToken).ConfigureAwait(false);
        if (!res && WebSocket is {State: WebSocketState.Open})
        {
            await Resubscribe(cancellationToken).ConfigureAwait(false);
        }

        return res;
    }

    protected override async Task DispatchMessage(TData? data, ReadOnlyMemory<byte> memory,
        CancellationToken cancellationToken)
    {
        if (data?.HasData ?? false)
        {
            switch (data.ArgChannel)
            {
                case ChannelName:
                    var fr = JsonSerializer.Deserialize<OkxWebsocketFundingRateResponse>(
                        memory.Span,
                        JsonSerializerOptions
                    );
                    foreach (var block in BufferBlocks)
                    {
                        await block!.SendAsync(fr, cancellationToken).ConfigureAwait(false);
                    }

                    break;
            }
        }


        await base.DispatchMessage(data, memory, cancellationToken);
    }

    protected override async Task<int> DoSubscribeOrUnsubscribe<TCollection>(bool isSubscription,
        TCollection subscriptions,
        CancellationToken cancellationToken)
    {
        var res = await base.DoSubscribeOrUnsubscribe(isSubscription, subscriptions, cancellationToken);
        if (res > 0)
        {
            if (isSubscription)
            {
                foreach (var subscription in subscriptions)
                {
                    ExpectedSubscriptions.TryAdd(subscription, default);
                }
            }
            else
            {
                foreach (var subscription in subscriptions)
                {
                    ExpectedSubscriptions.TryRemove(subscription, out _);
                }
            }
        }

        return res;
    }
}

public sealed class
    OkxWebsocketForFundingRate :
        OkxWebsocketForFundingRate<PreParsedOkxWebSocketMessage, OkxWebsocketForFundingRateOptions>,
        IService
{
    public OkxWebsocketForFundingRate(IOkxLimiterRegistry limiterRegistry, IConfiguration configuration, ILogger logger,
        IClientWebSocketFactory webSocketFactory, Boxed<CancellationToken> cancellationToken) : base(limiterRegistry,
        configuration, logger, webSocketFactory, cancellationToken) { }
}