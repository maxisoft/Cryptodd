using Cryptodd.Binance.Http;
using Cryptodd.Binance.Http.RateLimiter;
using Cryptodd.Binance.Orderbooks.Handlers;
using Cryptodd.Binance.Orderbooks.Websockets;
using Cryptodd.BinanceFutures.Http.Options;
using Lamar;
using Maxisoft.Utils.Collections.Lists;
using Maxisoft.Utils.Objects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Cryptodd.Binance.Orderbooks;

// ReSharper disable once ClassNeverInstantiated.Global
public sealed class BinanceOrderbookCollector : BaseBinanceOrderbookCollector<BinanceOrderbookWebsocket,
    BinanceOrderbookWebsocketOptions, BinanceFuturesPublicHttpApiCallOrderBookOptions, BinanceWebsocketCollection>
{
    public BinanceOrderbookCollector(IContainer container, ILogger logger, IConfiguration configuration,
        Boxed<CancellationToken> cancellationToken) : base(container, logger, configuration, cancellationToken)
    {
        if (string.IsNullOrEmpty(ConfigurationSection))
        {
            ConfigurationSection = "Binance:OrderbookCollector";
        }

        configuration.GetSection(ConfigurationSection).Bind(Options);
        if (string.IsNullOrEmpty(PairFilterName))
        {
            PairFilterName = "Binance:Orderbook";
        }

        var httpApi = container.GetRequiredService<IBinancePublicHttpApi>();
        RateLimiter = httpApi.RateLimiter;
        HttpOrderbookProvider = httpApi;
        SymbolLister = httpApi;
        Websockets = BinanceWebsocketCollection.Empty;
    }

    protected override IBinanceHttpOrderbookProvider HttpOrderbookProvider { get; }

    protected override IBinanceHttpSymbolLister SymbolLister { get; }
    protected override int MaxOrderBookLimit => IBinancePublicHttpApi.MaxOrderbookLimit;

    protected override IBinanceRateLimiter RateLimiter { get; }

    protected override async ValueTask DispatchToObHandlers(IServiceContext container,
        BinanceOrderbookHandlerArguments arg,
        CancellationToken cancellationToken)
    {
        var handlers = container.GetAllInstances<IBinanceOrderbookHandler>();
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
        var handlers = container.GetAllInstances<IBinanceAggregatedOrderbookHandler>();
        if (handlers.Count == 0)
        {
            return;
        }

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

    protected override BinanceWebsocketCollection CreateWebsockets(
        ArrayList<string> symbols, IServiceProvider serviceProvider)
    {
        BinanceOrderbookWebsocket WebsocketFactory()
        {
            var ws = serviceProvider.GetRequiredService<BinanceOrderbookWebsocket>();
            ws.RegisterDepthTargetBlock(TargetBlock);
            return ws;
        }

        var res = BinanceWebsocketCollection.Create(symbols, WebsocketFactory);
        return res;
    }
}