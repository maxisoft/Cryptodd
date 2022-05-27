using CryptoDumper.Ftx;
using CryptoDumper.Http;
using CryptoDumper.IoC.Registries;
using CryptoDumper.IoC.Registries.Customs;
using Lamar;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CryptoDumper.IoC
{
    public interface IContainerFactory
    {
        Container CreateContainer();
    }

    public class ContainerFactory : IContainerFactory
    {
        public Container CreateContainer()
        {
            var configurationRegistry = new ConfigurationServiceRegistry();
            var configuration = configurationRegistry.Configuration;
            var loggerRegistry = new LoggerServiceRegistry(configuration);
            var logger = loggerRegistry.Logger.ForContext(GetType());
            PluginRegistry? pluginRegistry = null;
            if (configuration.GetValue("LoadPlugins", true))
            {
                pluginRegistry = new PluginRegistry(configuration, loggerRegistry.Logger);
            }
            var container = new Container(x =>
            {
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
            });

            
            container.Configure(c =>
            {
                
                c.AddSingleton<IContainerFactory>(this);
                c.AddHttpClient<IFtxPublicHttpApi, FtxPublicHttpApi>((provider, client) => 
                    {
                        var httpClientFactoryHelper = provider.GetService<IHttpClientFactoryHelper>();
                        httpClientFactoryHelper?.Configure(client);
                    })
                    .ConfigurePrimaryHttpMessageHandler(provider => provider.GetService<IHttpClientFactoryHelper>()!.GetHandler())
                    .AddPolicyHandler((provider, _) => provider.GetService<IHttpClientFactoryHelper>()?.GetRetryPolicy());
                //.AddPolicyHandler(GetCircuitBreakerPolicy());
            });
            
            if (configuration.GetValue("DoubleCheckContainer", false))
            {
                container.AssertConfigurationIsValid();
            }
#if DEBUG
            // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
            logger.Debug(container.WhatDidIScan());
            // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
            logger.Debug(container.WhatDoIHave());
#endif

            logger.Verbose("Done creating container");
            return container;
        }
    }
}