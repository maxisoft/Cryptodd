using CryptoDumper.Ftx;
using Lamar;
using Microsoft.Extensions.DependencyInjection;

namespace CryptoDumper.IoC.Registries.Customs;

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