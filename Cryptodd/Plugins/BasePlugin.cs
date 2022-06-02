using Lamar;
using Serilog;

namespace Cryptodd.Plugins;

public abstract class BasePlugin : IBasePlugin
{
    protected BasePlugin(IContainer container)
    {
        Container = container;
        Logger = container.GetInstance<ILogger>().ForContext(GetType());
    }

    public ILogger Logger { get; protected set; }

    protected IContainer Container { get; }

    public virtual string Name => GetType().Name;
    public virtual Version Version => GetType().Assembly.GetName().Version ?? new Version();

    public virtual Task OnStart() => Task.CompletedTask;

    public int Order { get; protected set; } = 0;

    public virtual ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}