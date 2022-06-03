using Cryptodd.Ftx;
using Cryptodd.Http;
using Cryptodd.IoC.Registries;
using Cryptodd.IoC.Registries.Customs;
using Lamar;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cryptodd.IoC;

public interface IContainerFactory
{
    Container CreateContainer();
}

internal class CreateContainerOptions
{
    internal Action<ServiceRegistry> PostConfigure = registry => { };

    internal Action<ServiceRegistry> PreConfigure = registry => { };
    internal bool ScanForPlugins { get; set; } = true;

    internal ConfigurationServiceOptions ConfigurationServiceOptions { get; set; } = new();

    internal bool DebugPrint { get; set; } = true;
}

public class ContainerFactory : IContainerFactory
{
    public Container CreateContainer() => CreateContainer(new CreateContainerOptions());

    internal Container CreateContainer(CreateContainerOptions options)
    {
        var configurationRegistry = new ConfigurationServiceRegistry(options.ConfigurationServiceOptions);
        var configuration = configurationRegistry.Configuration;
        var loggerRegistry = new LoggerServiceRegistry(configuration);
        var logger = loggerRegistry.Logger.ForContext(GetType());
        PluginRegistry? pluginRegistry = null;
        if (options.ScanForPlugins && configuration.GetValue("LoadPlugins", true))
        {
            pluginRegistry = new PluginRegistry(configuration, loggerRegistry.Logger);
        }

        var container = new Container(x =>
        {
            options.PreConfigure(x);
            x.Scan(scanner =>
            {
                scanner.TheCallingAssembly();
                scanner.LookForRegistries();
                scanner.IncludeNamespaceContainingType<DefaultServiceRegistry>();
                scanner.ExcludeNamespaceContainingType<LoggerServiceRegistry>();
            });

            x.IncludeRegistry(configurationRegistry);
            x.IncludeRegistry(loggerRegistry);
            x.IncludeRegistry<MemoryCacheRegistry>();
            x.IncludeRegistry<RedisServiceRegistry>();
            x.IncludeRegistry<HandlerRegistry>();
            if (pluginRegistry is not null)
            {
                x.IncludeRegistry(pluginRegistry);
            }

            x.Injectable<IContainer>();

            x.Configure((IServiceCollection c) =>
            {
                c.AddSingleton<IContainerFactory>(this);
                c.AddHttpClient<IFtxPublicHttpApi, FtxPublicHttpApi>((provider, client) =>
                    {
                        var httpClientFactoryHelper = provider.GetService<IHttpClientFactoryHelper>();
                        httpClientFactoryHelper?.Configure(client);
                    })
                    .ConfigurePrimaryHttpMessageHandler(provider =>
                        provider.GetService<IHttpClientFactoryHelper>()!.GetHandler())
                    .AddPolicyHandler(
                        (provider, _) => provider.GetService<IHttpClientFactoryHelper>()?.GetRetryPolicy());
                //.AddPolicyHandler(GetCircuitBreakerPolicy());
            });

            options.PostConfigure(x);
        });

        if (configuration.GetValue("DoubleCheckContainer", false))
        {
            container.AssertConfigurationIsValid();
        }
#if DEBUG
        if (options.DebugPrint)
        {
            // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
            logger.Debug(container.WhatDidIScan());
            // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
            logger.Debug(container.WhatDoIHave());
        }

#endif
        logger.Verbose("Done creating container");
        return container;
    }
}