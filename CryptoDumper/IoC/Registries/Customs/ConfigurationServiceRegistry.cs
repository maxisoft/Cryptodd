using System.Reflection;
using Lamar;
using Maxisoft.Utils.Collections.Dictionaries;
using Microsoft.Extensions.Configuration;
using static CryptoDumper.Const;

namespace CryptoDumper.IoC.Registries.Customs
{
    public class ConfigurationServiceRegistry : ServiceRegistry
    {
        public ConfigurationServiceRegistry()
        {
            Configuration = BuildConfiguration();

            ForSingletonOf<IConfiguration>().Use(Configuration);
            ForSingletonOf<IConfigurationRoot>().Use(Configuration);
        }

        internal IConfigurationRoot Configuration { get; }

        private static IConfigurationRoot BuildConfiguration()
        {
            var envConfig = new ConfigurationBuilder().AddEnvironmentVariables(EnvPrefix).Build();

            var basePath = envConfig.GetValue("BasePath", GetDefaultDataPath());
            var workingDirectory = Directory.GetCurrentDirectory();
            if (!File.Exists(Path.Combine(workingDirectory, ApplicationSettingsJsonFileName)))
            {
                workingDirectory = Directory.GetParent(Assembly.GetCallingAssembly().Location)!.FullName;
            }

            var defaultConfig = new OrderedDictionary<string, string>(1);

            if (basePath == GetDefaultDataPath() && !Directory.Exists(basePath))
            {
                Directory.CreateDirectory(basePath);
            }

            defaultConfig["BasePath"] = basePath;

            return new ConfigurationBuilder()
                .AddInMemoryCollection(defaultConfig)
                .SetBasePath(basePath)
                .AddJsonFile(Path.GetFullPath(Path.Combine(workingDirectory, ApplicationSettingsJsonFileName)), true)
                .AddEnvironmentVariables(EnvPrefix)
                .AddYamlFile(Path.GetFullPath(Path.Combine(workingDirectory, ApplicationConfigYamlFileName)), false)
                .Build();
        }

        private static string GetDefaultDataPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ApplicationName);
        }
    }
}