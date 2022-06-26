using Cryptodd.Bitfinex.Orderbooks;
using Cryptodd.Ftx;
using Cryptodd.Ftx.Orderbooks;
using Cryptodd.Ftx.Orderbooks.RegroupedOrderbooks;
using Lamar;

namespace Cryptodd.IoC.Registries;

public class DefaultServiceRegistry : ServiceRegistry
{
    public DefaultServiceRegistry()
    {
        Scan(scanner =>
        {
            scanner.TheCallingAssembly();
            scanner.ExcludeType<INoAutoRegister>();
            scanner.ExcludeType<IGroupedOrderbookHandler>();
            scanner.ExcludeType<IRegroupedOrderbookHandler>();
            scanner.ExcludeType<IOrderbookHandler>();
            scanner.AddAllTypesOf<IService>();
            scanner.SingleImplementationsOfInterface();
            scanner.WithDefaultConventions();
        });
    }
}