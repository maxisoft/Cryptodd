using Cryptodd.Bitfinex.Orderbooks;
using Cryptodd.Ftx;
using Cryptodd.Ftx.Futures;
using Cryptodd.Ftx.Orderbooks;
using Cryptodd.Ftx.Orderbooks.RegroupedOrderbooks;
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
        });
    }
}