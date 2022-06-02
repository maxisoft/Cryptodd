using System.Reflection;
using Lamar;
using Maxisoft.Utils.Collections.Dictionaries;
using Microsoft.Extensions.Configuration;
using static CryptoDumper.Const;

namespace CryptoDumper.IoC.Registries.Customs;

internal class ConfigurationServiceOptions
{
    internal string? DefaultBasePath { get; set; }
    internal string? WorkingDirectory { get; set; }

    internal OrderedDictionary<string, string> DefaultConfig { get; set; } = new OrderedDictionary<string, string>();

    internal bool ScanForAssemblyConfig { get; set; } = true;
    internal bool ScanForWorkingDirectoryConfig { get; set; } = true;

    internal bool ScanForEnvConfig { get; set; } = true;
}

public class ConfigurationServiceRegistry : ServiceRegistry
{
    public ConfigurationServiceRegistry() : this(new ConfigurationServiceOptions()) { }

    internal ConfigurationServiceRegistry(ConfigurationServiceOptions options)
    {
        Options = options;
        Configuration = BuildConfiguration(options);

        ForSingletonOf<IConfiguration>().Use(Configuration);
        ForSingletonOf<IConfigurationRoot>().Use(Configuration);
    }

    internal IConfigurationRoot Configuration { get; }
    internal ConfigurationServiceOptions Options { get; }

    private static IConfigurationRoot BuildConfiguration(ConfigurationServiceOptions options)
    {
        var envConfig = new ConfigurationBuilder().AddEnvironmentVariables(EnvPrefixShort)
            .AddEnvironmentVariables(EnvPrefix).Build();

        var defaultBasePath = options.DefaultBasePath ?? GetDefaultDataPath();
        var basePath = envConfig.GetValue("BASEPATH", envConfig.GetValue("BasePath", defaultBasePath));
        var workingDirectory = options.WorkingDirectory ?? Directory.GetCurrentDirectory();
        var assemblyDirectory = Directory.GetParent(Assembly.GetCallingAssembly().Location)!.FullName;

        var defaultConfig = new OrderedDictionary<string, string>(2 + options.DefaultConfig.Count);
        foreach (var kv in options.DefaultConfig)
        {
            defaultConfig.TryAdd(kv.Key, kv.Value);
        }

        if (basePath == defaultBasePath && !Directory.Exists(basePath))
        {
            Directory.CreateDirectory(basePath);
        }

        defaultConfig.TryAdd("BasePath", basePath);
        defaultConfig.TryAdd("Docker", "" + ((Environment.GetEnvironmentVariable("IS_DOCKER") ?? "") == "1"));

        IConfigurationBuilder builder = new ConfigurationBuilder();

        builder = builder
            .AddInMemoryCollection(defaultConfig)
            .SetBasePath(new DirectoryInfo(basePath).FullName);
        if (options.ScanForAssemblyConfig)
        {
            builder = builder
                .AddJsonFile(Path.GetFullPath(Path.Combine(assemblyDirectory, ApplicationSettingsJsonFileName)), true)
                .AddYamlFile(Path.GetFullPath(Path.Combine(assemblyDirectory, ApplicationConfigYamlFileName)), true);
        }

        if (options.ScanForEnvConfig)
        {
            builder = builder.AddEnvironmentVariables(EnvPrefixShort)
                .AddEnvironmentVariables(EnvPrefix);
        }
        
        if (options.ScanForWorkingDirectoryConfig)
        {
            builder = builder
                .AddJsonFile(Path.GetFullPath(Path.Combine(workingDirectory, ApplicationSettingsJsonFileName)), true)
                .AddYamlFile(Path.GetFullPath(Path.Combine(workingDirectory, ApplicationConfigYamlFileName)), true);
        }

        return builder.Build();
    }

    private static string GetDefaultDataPath()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ApplicationName);
    }
}