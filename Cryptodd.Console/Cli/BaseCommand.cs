using Cryptodd.Databases.Postgres;
using Cryptodd.IoC;
using Cryptodd.Plugins;
using Cryptodd.Scheduler.Tasks;
using Lamar;
using Maxisoft.Utils.Disposables;
using Maxisoft.Utils.Objects;
using Microsoft.Extensions.Configuration;
using Serilog;
using Typin;
using Typin.Console;
using TaskScheduler = Cryptodd.Scheduler.TaskScheduler;

namespace Cryptodd.Cli;

public class BaseCommandOptions
{
    public bool LoadPlugins { get; set; } = true;

    public TimeSpan GlobalTimeout { get; set; } = TimeSpan.MaxValue;

    public bool BindToConfiguration { get; set; } = true;

    public bool SetupTimescaleDb { get; set; } = true;

    public bool SetupScheduler { get; set; } = true;
}

public abstract class BaseCommand<TOptions> : ICommand, IDisposable where TOptions : BaseCommandOptions, new()
{
    private readonly Lazy<INestedContainer> _container;

    private readonly Lazy<Container> _rootContainer;

    protected internal readonly DisposableManager DisposableManager = new();

    protected TaskScheduler? TaskScheduler;

    public BaseCommand()
    {
        _rootContainer = new Lazy<Container>(() =>
            new ContainerFactory().CreateContainer(
                new CreateContainerOptions { ScanForPlugins = Options.LoadPlugins }));
        _container = new Lazy<INestedContainer>(() =>
        {
            var container = RootContainer.GetNestedContainer();
            try
            {
                container.Inject((IContainer)container);
            }
            catch (Exception e)
            {
                container.Dispose();
                throw;
            }

            DisposableManager.LinkDisposable(container);
            return container;
        });
    }

    protected TOptions Options { get; set; } = new();

    internal Container RootContainer => _rootContainer.Value;

    protected internal virtual INestedContainer Container => _container.Value;

    protected internal ILogger Logger => Container.GetInstance<ILogger>().ForContext(GetType());
    protected internal IConfiguration Configuration => Container.GetInstance<IConfiguration>();

    protected internal string WorkingDirectory =>
        Configuration.GetValue("BasePath", Environment.CurrentDirectory);

    protected virtual string ConfigurationKey => string.Empty;

    public abstract ValueTask ExecuteAsync(IConsole console);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }


    protected virtual async ValueTask<int> LoadPlugins()
    {
        var res = 0;

        foreach (var plugin in Container.GetAllInstances<IBasePlugin>().OrderBy(plugin => plugin.Order))
        {
            try
            {
                await plugin.OnStart().ConfigureAwait(true);
            }
            catch (Exception e)
            {
                Logger.Fatal(e, "Plugin {Plugin} of type {Type} error on start", plugin.Name,
                    plugin.GetType().FullName);
                await plugin.DisposeAsync();
                throw;
            }

            res += 1;
        }

        return res;
    }

    protected virtual async Task PreExecute()
    {
        if (Options.BindToConfiguration)
        {
            if (string.IsNullOrEmpty(ConfigurationKey))
            {
                Configuration.Bind(Options);
            }
            else
            {
                Configuration.GetSection(ConfigurationKey).Bind(Options);
            }
        }

        var cwd = WorkingDirectory;
        if (cwd != Environment.CurrentDirectory)
        {
            Logger.Information("setting CurrentDirectory to {CurrentDirectory}", cwd);
        }

        Environment.CurrentDirectory = Path.GetFullPath(cwd);

        var container = Container;
        var cancellationToken = container.GetInstance<Boxed<CancellationToken>>();


        if (Options.GlobalTimeout != TimeSpan.MaxValue)
        {
            Logger.Verbose("Setting app execution timeout to {Timeout}", Options.GlobalTimeout);
            var cancellationTokenSource = container.GetInstance<CancellationTokenSource>();
            cancellationTokenSource.CancelAfter(Options.GlobalTimeout);
        }

        if (Options.LoadPlugins)
        {
            Logger.Verbose("Loading plugins");
            await LoadPlugins();
        }

        if (Options.SetupTimescaleDb)
        {
            await container.GetInstance<TimescaleDB>().Setup(cancellationToken);
        }

        if (Options.SetupScheduler)
        {
            TaskScheduler = container.GetInstance<TaskScheduler>();
            var schedTasks = container.GetAllInstances<BaseScheduledTask>();
            foreach (var schedTask in schedTasks)
            {
                TaskScheduler.RegisterTask(schedTask);
            }
        }
    }

    protected async ValueTask SchedulerLoop(int sleepDuration = 300)
    {
        var cancellationToken = Container.GetInstance<Boxed<CancellationToken>>().Value;
        while (!cancellationToken.IsCancellationRequested)
        {
            await TaskScheduler!.Tick(cancellationToken);
            await Task.Delay(sleepDuration, cancellationToken);
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposableManager.Dispose();
            if (_rootContainer.IsValueCreated)
            {
                _rootContainer.Value.Dispose();
            }
        }
    }
}

public abstract class BaseCommand : BaseCommand<BaseCommandOptions> { }