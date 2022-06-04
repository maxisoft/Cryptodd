using Cryptodd.Scheduler;
using Cryptodd.Scheduler.Tasks;
using Lamar;
using Microsoft.Extensions.DependencyInjection;

namespace Cryptodd.IoC.Registries;

public class ScheduledTaskRegistry : ServiceRegistry
{
    public ScheduledTaskRegistry()
    {
        Scan(scanner =>
        {
            scanner.ExcludeType<INoAutoRegister>();
            scanner.TheCallingAssembly();
            scanner.AddAllTypesOf<ScheduledTask>(ServiceLifetime.Transient);
        });
    }
}