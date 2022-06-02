using Lamar;
using Lamar.IoC.Instances;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Exceptions;

namespace Cryptodd.IoC.Registries.Customs
{
    public class LoggerServiceRegistry : ServiceRegistry
    {
        public LoggerServiceRegistry(IConfiguration configuration)
        {
            // ReSharper disable once UnreachableCode
            var loggingLevel = new LoggingLevelSwitch();
            var serilogConfig = GetSerilogConfig(configuration, loggingLevel);

            Logger = serilogConfig.CreateLogger();

            ForSingletonOf<LoggingLevelSwitch>().Use(loggingLevel);
            ForSingletonOf<ILogger>().Use(Logger);
#if DEBUG
            loggingLevel.MinimumLevel = LogEventLevel.Debug;
            ForSingletonOf<LoggerConfiguration>().Use(serilogConfig);
            ForSingletonOf<Logger>().Use(Logger);
#endif

            Policies.Add(new SerilogInstancePolicy(Logger));
        }

        internal Logger Logger { get; }

        private static LoggerConfiguration GetSerilogConfig(IConfiguration configuration,
            LoggingLevelSwitch loggingLevel) =>
            new LoggerConfiguration()
#if DEBUG
                .Enrich.WithExceptionDetails()
#endif
                .MinimumLevel.ControlledBy(loggingLevel)
                .WriteTo.Console(
                    outputTemplate:
                    "[{Timestamp:HH:mm:ss} {SourceContext:l} {Level}] {Message}{NewLine}{Exception}")
                .Enrich.FromLogContext()
                .WriteTo.Async(a =>
                    a.RollingFile("logs/{Date}.txt"))
                .ReadFrom.Configuration(configuration);


        public class SerilogInstancePolicy : ConfiguredInstancePolicy
        {
            private readonly ILogger _logger;

            public SerilogInstancePolicy(ILogger logger)
            {
                _logger = logger;
            }

            protected override void apply(IConfiguredInstance instance)
            {
                if (instance is ConstructorInstance ci)
                {
                    if (ci.Arguments.Any(arg => typeof(ILogger).IsAssignableFrom(arg.Parameter.ParameterType)))
                    {
                        instance.AddInline(new LambdaInstance(typeof(ILogger),
                            _ => _logger.ForContext(instance.ImplementationType),
                            ServiceLifetime.Singleton));
                    }
                }
                else if (instance.Constructor
                    ?.GetParameters().Any(param => typeof(ILogger).IsAssignableFrom(param.ParameterType)) ?? false)
                {
                    instance.AddInline(new LambdaInstance(typeof(ILogger),
                        _ => _logger.ForContext(instance.ImplementationType),
                        ServiceLifetime.Singleton));
                }
            }
        }
    }
}