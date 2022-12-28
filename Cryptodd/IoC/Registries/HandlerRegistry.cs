using Cryptodd.Binance.Orderbooks.Handlers;
using Cryptodd.Bitfinex.Orderbooks;
using Cryptodd.Ftx;
using Cryptodd.Ftx.Futures;
using Cryptodd.Ftx.Orderbooks;
using Cryptodd.Ftx.Orderbooks.RegroupedOrderbooks;
using Cryptodd.Okx.Orderbooks.Handlers;
using Lamar;
using Microsoft.Extensions.DependencyInjection;

namespace Cryptodd.IoC.Registries;

public class HandlerRegistry : ServiceRegistry
{
    public HandlerRegistry()
    {
        Scan(scanner =>
        {
            scanner.ExcludeType<INoAutoRegister>();
            scanner.TheCallingAssembly();
            scanner.AddAllTypesOf<IGroupedOrderbookHandler>(ServiceLifetime.Transient);
            scanner.AddAllTypesOf<IRegroupedOrderbookHandler>(ServiceLifetime.Transient);
            scanner.AddAllTypesOf<IFuturesStatsHandler>(ServiceLifetime.Transient);
            scanner.AddAllTypesOf<IOrderbookHandler>(ServiceLifetime.Transient);
            
            scanner.AddAllTypesOf<IBinanceOrderbookHandler>(ServiceLifetime.Transient);
            scanner.AddAllTypesOf<IBinanceAggregatedOrderbookHandler>(ServiceLifetime.Transient);
            
            scanner.AddAllTypesOf<IOkxOrderbookHandler>(ServiceLifetime.Transient);
            scanner.AddAllTypesOf<IOkxGroupedOrderbookHandler>(ServiceLifetime.Transient);
        });
    }
}