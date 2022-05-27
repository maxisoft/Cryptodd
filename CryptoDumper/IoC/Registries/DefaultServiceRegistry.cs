using CryptoDumper.Ftx;
using Lamar;

namespace CryptoDumper.IoC.Registries
{
    public class DefaultServiceRegistry : ServiceRegistry
    {
        public DefaultServiceRegistry()
        {
            Scan(scanner =>
            {
                scanner.TheCallingAssembly();
                scanner.ExcludeType<INoAutoRegister>();
                scanner.ExcludeType<IGroupedOrderbookHandler>();
                scanner.AddAllTypesOf<IService>();
                scanner.SingleImplementationsOfInterface();
                scanner.WithDefaultConventions();
            });
        }
    }
}