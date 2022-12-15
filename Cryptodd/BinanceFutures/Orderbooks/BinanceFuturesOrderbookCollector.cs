using Cryptodd.Binance.Http;
using Cryptodd.Binance.Http.RateLimiter;
using Cryptodd.Binance.Orderbooks;
using Cryptodd.Binance.Orderbooks.Handlers;
using Cryptodd.BinanceFutures.Http;
using Cryptodd.BinanceFutures.Http.Options;
using Cryptodd.BinanceFutures.Orderbooks.Handlers;
using Cryptodd.BinanceFutures.Orderbooks.Websockets;
using Lamar;
using Maxisoft.Utils.Collections.Lists;
using Maxisoft.Utils.Objects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Cryptodd.BinanceFutures.Orderbooks;

// ReSharper disable once UnusedType.Global
// ReSharper disable once ClassNeverInstantiated.Global
public sealed class BinanceFuturesOrderbookCollector : BaseBinanceOrderbookCollector<BinanceFuturesOrderbookWebsocket,
    BinanceFuturesOrderbookWebsocketOptions, BinanceFuturesPublicHttpApiCallOrderBookOptions,
    BinanceFuturesWebsocketCollection>
{
    public BinanceFuturesOrderbookCollector(IContainer container, ILogger logger, IConfiguration configuration,
        Boxed<CancellationToken> cancellationToken) : base(container, logger, configuration, cancellationToken)
    {
        if (string.IsNullOrEmpty(ConfigurationSection))
        {
            ConfigurationSection = "BinanceFutures:OrderbookCollector";
        }

        configuration.GetSection(ConfigurationSection).Bind(Options);
        if (string.IsNullOrEmpty(PairFilterName))
        {
            PairFilterName = "BinanceFutures:Orderbook";
        }

        var httpApi = container.GetRequiredService<IBinanceFuturesPublicHttpApi>();
        RateLimiter = httpApi.RateLimiter;
        HttpOrderbookProvider = httpApi;
        SymbolLister = httpApi;
        Websockets = BinanceFuturesWebsocketCollection.Empty;
    }

    protected override IBinanceHttpOrderbookProvider HttpOrderbookProvider { get; }

    protected override IBinanceHttpSymbolLister SymbolLister { get; }
    protected override int MaxOrderBookLimit => IBinanceFuturesPublicHttpApi.MaxOrderbookLimit;

    protected override IBinanceRateLimiter RateLimiter { get; }

    protected override async ValueTask DispatchToObHandlers(IServiceContext container,
        BinanceOrderbookHandlerArguments arg,
        CancellationToken cancellationToken)
    {
        var handlers = container.GetAllInstances<IBinanceFuturesOrderbookHandler>();
        if (handlers.Count == 0)
        {
            return;
        }

        var tasks = handlers.Select(handler => handler.Handle(arg, cancellationToken)).ToArray();

        await WaitForHandlers("Raw Orderbooks", handlers, tasks, cancellationToken);
    }

    protected override async ValueTask CreateAggregateAndDispatch(IServiceContext container,
        BinanceOrderbookHandlerArguments arg,
        CancellationToken cancellationToken)
    {
        var handlers = container.GetAllInstances<IBinanceFuturesAggregatedOrderbookHandler>();
        if (handlers.Count > 0)
        {
            var aggregator =
                container.GetRequiredService<IOrderbookAggregator>();
            var aggregate = await aggregator.Handle(arg, cancellationToken);
            
            if (aggregate.DateTime <= DateTimeOffset.UnixEpoch)
            {
                Logger.Warning("invalid Datetime for {Symbol} orderbook", arg.Symbol);
                return;
            }

            var tasks = handlers.Select(handler => handler.Handle(aggregate, cancellationToken)).ToArray();
            await WaitForHandlers("Aggregated Orderbooks", handlers, tasks, cancellationToken);
        }
    }

    protected override BinanceFuturesWebsocketCollection CreateWebsockets(
        ArrayList<string> symbols, IServiceProvider serviceProvider)
    {
        BinanceFuturesOrderbookWebsocket WebsocketFactory()
        {
            var ws = serviceProvider.GetRequiredService<BinanceFuturesOrderbookWebsocket>();
            ws.RegisterDepthTargetBlock(TargetBlock);
            return ws;
        }

        var res = BinanceFuturesWebsocketCollection.Create(symbols, WebsocketFactory);
        return res;
    }
}