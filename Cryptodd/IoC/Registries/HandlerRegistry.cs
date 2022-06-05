using Cryptodd.Ftx;
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
        });
        
        Scan(scanner =>
        {
            scanner.ExcludeType<INoAutoRegister>();
            scanner.TheCallingAssembly();
            scanner.AddAllTypesOf<IRegroupedOrderbookHandler>(ServiceLifetime.Transient);
        });
    }
}