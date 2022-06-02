using Cryptodd.Ftx;
using Lamar;
using Microsoft.Extensions.DependencyInjection;

namespace Cryptodd.IoC.Registries.Customs;

public class HandlerRegistry : ServiceRegistry
{
    public HandlerRegistry()
    {
        Scan(scanner =>
        {
            scanner.ExcludeType<INoAutoRegister>();
            scanner.TheCallingAssembly();
            scanner.AddAllTypesOf<IGroupedOrderbookHandler>(ServiceLifetime.Scoped);
        });
    }
}