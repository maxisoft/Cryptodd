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
                scanner.AddAllTypesOf<IService>();
                scanner.SingleImplementationsOfInterface();
                scanner.WithDefaultConventions();
            });
        }
    }
}