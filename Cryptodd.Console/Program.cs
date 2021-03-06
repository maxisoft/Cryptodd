using System.Text;
using Cryptodd.Ftx;
using Cryptodd.IoC;
using Cryptodd.Plugins;
using Cryptodd.Scheduler;
using Cryptodd.Scheduler.Tasks;
using Lamar;
using Maxisoft.Utils.Objects;
using TaskScheduler = Cryptodd.Scheduler.TaskScheduler;

namespace Cryptodd.Console;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var rootContainer = new ContainerFactory().CreateContainer();

        using var container = rootContainer.GetNestedContainer();
        container.Inject((IContainer)container);
        foreach (var plugin in container.GetAllInstances<IBasePlugin>().OrderBy(plugin => plugin.Order))
        {
            await plugin.OnStart();
        }
        
        var cancellationToken = container.GetInstance<Boxed<CancellationToken>>();
        
        var sched = container.GetInstance<TaskScheduler>();
        var schedTasks = container.GetAllInstances<ScheduledTask>();
        foreach (var schedTask in schedTasks)
        {
            sched.RegisterTask(schedTask);
        }

        while (!cancellationToken.Value.IsCancellationRequested)
        {
            await sched.Tick(cancellationToken);
            await Task.Delay(300);
        }

    }
}