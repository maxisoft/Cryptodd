﻿using Cryptodd.Binance;
using Cryptodd.Binance.Http;
using Cryptodd.Binance.Http.RateLimiter;
using Cryptodd.BinanceFutures.Http;
using Cryptodd.BinanceFutures.Http.RateLimiter;
using Cryptodd.Bitfinex;
using Cryptodd.Bitfinex.Http;
using Cryptodd.Bitfinex.Http.Abstractions;
using Cryptodd.Features;
using Cryptodd.Ftx;
using Cryptodd.Http;
using Cryptodd.IoC.Registries;
using Cryptodd.IoC.Registries.Customs;
using Cryptodd.Okx.Http;
using Cryptodd.Okx.Http.Abstractions;
using Cryptodd.TradeAggregates;
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
        var featureList = new FeatureList();
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
            if (configuration.GetSection("Postgres").GetValue<bool>("Enabled",
                    !string.IsNullOrWhiteSpace(
                        configuration.GetSection("Postgres").GetValue<string>("ConnectionString", ""))))
            {
                featureList.RegisterFeature(ExternalFeatureFlags.Postgres);
                x.IncludeRegistry<PostgresDatabaseRegistry>();
            }

            if (configuration.GetSection("Sqlite").GetValue<bool>("Enabled", false))
            {
                featureList.RegisterFeature(ExternalFeatureFlags.Sqlite);
                x.IncludeRegistry<SqliteDatabaseRegistry>();
            }
            
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
                        (provider, _) => provider.GetService<IHttpClientFactoryHelper>()?.GetRetryPolicy())
                    .SetHandlerLifetime(TimeSpan.FromMinutes(5));

                c.AddHttpClient<IBitfinexHttpClientAbstraction, BitfinexHttpClientAbstraction>((provider, client) =>
                    {
                        var httpClientFactoryHelper = provider.GetService<IHttpClientFactoryHelper>();
                        httpClientFactoryHelper?.Configure(client);
                    })
                    .ConfigurePrimaryHttpMessageHandler(provider =>
                        provider.GetService<IHttpClientFactoryHelper>()!.GetHandler())
                    .AddPolicyHandler(
                        (provider, _) => provider.GetService<IHttpClientFactoryHelper>()?.GetRetryPolicy())
                    .SetHandlerLifetime(TimeSpan.FromMinutes(5));
                
                c.AddHttpClient<IBinanceHttpClientAbstraction, BinanceHttpClientAbstraction>((provider, client) =>
                    {
                        var httpClientFactoryHelper = provider.GetService<IHttpClientFactoryHelper>();
                        httpClientFactoryHelper?.Configure(client);
                    })
                    .ConfigurePrimaryHttpMessageHandler(provider =>
                        provider.GetService<IHttpClientFactoryHelper>()!.GetHandler())
                    .AddPolicyHandler(
                        (provider, _) => provider.GetService<IHttpClientFactoryHelper>()?.GetRetryPolicy())
                    .SetHandlerLifetime(TimeSpan.FromMinutes(5));
                
                c.AddHttpClient<IBinanceFuturesHttpClientAbstraction, BinanceFuturesHttpClientAbstraction>((provider, client) =>
                    {
                        var httpClientFactoryHelper = provider.GetService<IHttpClientFactoryHelper>();
                        httpClientFactoryHelper?.Configure(client);
                    })
                    .ConfigurePrimaryHttpMessageHandler(provider =>
                        provider.GetService<IHttpClientFactoryHelper>()!.GetHandler())
                    .AddPolicyHandler(
                        (provider, _) => provider.GetService<IHttpClientFactoryHelper>()?.GetRetryPolicy())
                    .SetHandlerLifetime(TimeSpan.FromMinutes(5));
                
                c.AddHttpClient<IOkxHttpClientAbstraction, OkxHttpClientAbstraction>((provider, client) =>
                    {
                        var httpClientFactoryHelper = provider.GetService<IHttpClientFactoryHelper>();
                        httpClientFactoryHelper?.Configure(client);
                    })
                    .ConfigurePrimaryHttpMessageHandler(provider =>
                        provider.GetService<IHttpClientFactoryHelper>()!.GetHandler())
                    .AddPolicyHandler(
                        (provider, _) => provider.GetService<IHttpClientFactoryHelper>()?.GetRetryPolicy())
                    .SetHandlerLifetime(TimeSpan.FromMinutes(5));
            });
            

            x.Use(featureList).Singleton()
                .For<IFeatureList>()
                .For<IFeatureListRegistry>();

            x.ForSingletonOf<IInternalBinanceRateLimiter>().Use<BinanceRateLimiter>();
            x.For<IBinanceRateLimiter>().Use(context => context.GetInstance<IInternalBinanceRateLimiter>());
            
            x.ForSingletonOf<IInternalBinanceFuturesRateLimiter>().Use<BinanceFuturesRateLimiter>();
            x.For<IBinanceFuturesRateLimiter>().Use(context => context.GetInstance<IInternalBinanceFuturesRateLimiter>());

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