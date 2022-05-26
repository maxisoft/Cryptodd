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
            var envConfig = new ConfigurationBuilder().AddEnvironmentVariables(EnvPrefixShort).AddEnvironmentVariables(EnvPrefix).Build();

            var basePath = envConfig.GetValue("BASEPATH", envConfig.GetValue("BasePath", GetDefaultDataPath()));
            var workingDirectory = Directory.GetCurrentDirectory();
            var assemblyDirectory = Directory.GetParent(Assembly.GetCallingAssembly().Location)!.FullName;

            var defaultConfig = new OrderedDictionary<string, string>(1);

            if (basePath == GetDefaultDataPath() && !Directory.Exists(basePath))
            {
                Directory.CreateDirectory(basePath);
            }

            defaultConfig["BasePath"] = basePath;
            defaultConfig["Docker"] = "" + ((Environment.GetEnvironmentVariable("IS_DOCKER") ?? "") == "1");

            return new ConfigurationBuilder()
                .AddInMemoryCollection(defaultConfig)
                .SetBasePath(new DirectoryInfo(basePath).FullName)
                .AddJsonFile(Path.GetFullPath(Path.Combine(assemblyDirectory, ApplicationSettingsJsonFileName)), true)
                .AddYamlFile(Path.GetFullPath(Path.Combine(assemblyDirectory, ApplicationConfigYamlFileName)), true)
                .AddJsonFile(Path.GetFullPath(Path.Combine(workingDirectory, ApplicationSettingsJsonFileName)), true)
                .AddYamlFile(Path.GetFullPath(Path.Combine(workingDirectory, ApplicationConfigYamlFileName)), true)
                .AddEnvironmentVariables(EnvPrefixShort)
                .AddEnvironmentVariables(EnvPrefix)
                .AddYamlFile(ApplicationSettingsJsonFileName, true)
                .AddYamlFile(ApplicationConfigYamlFileName, true)
                .Build();
        }

        private static string GetDefaultDataPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ApplicationName);
        }
    }
}