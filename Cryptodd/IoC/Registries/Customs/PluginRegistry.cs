using System.Collections.Immutable;
using System.Reflection;
using Cryptodd.Ftx;
using Cryptodd.Plugins;
using Lamar;
using Maxisoft.Plugins.Loader;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Cryptodd.IoC.Registries.Customs;

public interface IPluginRegistry
{
    ImmutableArray<Assembly> PluginsAssemblies { get; }
}

public class PluginRegistry : ServiceRegistry, IPluginRegistry
{
    private readonly IAssemblyReferenceCollector _assemblyReferenceCollector = new AssemblyReferenceCollector();
    private readonly IPluginCompiler _pluginCompiler;
    internal readonly ILogger Log;

    public PluginRegistry(IConfiguration configuration, ILogger logger)
    {
        Log = logger.ForContext(GetType());
        PluginDirectory = Path.Combine(configuration.GetValue<string>("BasePath"), "plugins");
        _pluginCompiler = new PluginCompiler(_assemblyReferenceCollector);
        ForSingletonOf<IAssemblyReferenceCollector>().Use(_assemblyReferenceCollector);
        ForSingletonOf<IPluginCompiler>().Use(_pluginCompiler);
        ForSingletonOf<IPluginRegistry>().Use(this);
        CompileAllPluginsAsync().Wait();

        if (PluginsAssemblies.Any())
        {
            Scan(scanner =>
            {
                foreach (var assembly in PluginsAssemblies)
                {
                    scanner.Assembly(assembly);
                }

                scanner.ExcludeType<INoAutoRegister>();
                scanner.AddAllTypesOf<IPluginService>();
                scanner.AddAllTypesOf<IBasePlugin>(ServiceLifetime.Singleton);
                scanner.AddAllTypesOf<IGroupedOrderbookHandler>();
            });
        }
    }

    public string PluginDirectory { get; }
    public ImmutableArray<Assembly> PluginsAssemblies { get; internal set; } = ImmutableArray<Assembly>.Empty;

    public async Task CompileAllPluginsAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(PluginDirectory))
        {
            Log.Warning("the plugins directory doesn't exists");
            return;
        }

        var subDirectories = Directory.GetDirectories(PluginDirectory);
        if (!subDirectories.Any())
        {
            Log.Debug("the plugins directory is empty");
            return;
        }

        Log.Debug("Starting the plugins compilation");

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(5));

        async Task<CompilerResult?> StartSafeCompileAsync(string directory)
        {
            cts.Token.ThrowIfCancellationRequested();
            try
            {
                var ret = await _pluginCompiler.CompileAsync(directory, GetType().Assembly, cts.Token);
                try
                {
                    cts.Token.ThrowIfCancellationRequested();
                    if (ret.Errors.Any() || ret.Assembly is null)
                    {
                        throw new Exception("there's still some error while compiling " +
                                            ret.CompilerContext.AssemblyName);
                    }
                }
                catch (Exception)
                {
                    ret.Dispose();
                    throw;
                }

                return ret;
            }
            catch (Exception e)
            {
                Log.Error(e, "unable to compile plugin {Name}", Path.GetFileName(directory));
                return null;
            }
        }

        var loadPluginTasks = new Task<CompilerResult?>[subDirectories.Length];

        {
            var i = 0;
            foreach (var subDir in subDirectories)
            {
                Task<CompilerResult?> task;

                if (Directory.EnumerateFiles(subDir, "*.cs", SearchOption.AllDirectories).Any())
                {
                    task = StartSafeCompileAsync(subDir);
                }
                else
                {
                    task = Task.FromResult<CompilerResult?>(null);
                }

                loadPluginTasks[i++] = task;
            }
        }

        foreach (var task in loadPluginTasks)
        {
            cts.Token.ThrowIfCancellationRequested();
            await task;
        }

        Assembly PostCompilation(CompilerResult compilerResult)
        {
            try
            {
                if (compilerResult.Errors!.Any() || compilerResult.Assembly is null)
                {
                    throw new Exception("there's still some error while compiling " +
                                        compilerResult.CompilerContext.AssemblyName);
                }

                if (compilerResult.Warnings.Any())
                {
                    Log.Warning("There's some warning while compiling {Plugin} :",
                        compilerResult.CompilerContext.AssemblyName);
                    foreach (var warning in compilerResult.Warnings)
                    {
                        Log.Warning(warning.ToString());
                    }
                }

                Log.Information("Loaded plugin {Plugin}", compilerResult.CompilerContext.AssemblyName);
                return compilerResult.Assembly;
            }
            finally
            {
                compilerResult.Dispose();
            }
        }

        PluginsAssemblies = loadPluginTasks.Where(task => task.IsCompleted && task.Result is not null)
            .Select(task => PostCompilation(task.Result!)).ToImmutableArray();
    }
}