
namespace CryptoDumper
{
    public static class Const
    {
        public const string ApplicationName = "Crypto-Dumper";
        public const string EnvPrefix = "CRYPTODUMPER_";
        public const string EnvPrefixShort = "CD_";
        public const string ApplicationSettingsJsonFileName = "appsettings.json";
        public const string ApplicationConfigYamlFileName = "config.yaml";
        public const string DatabaseExtension = ".db";

        public const bool IsDebug =
#if DEBUG
                true
#else
                false
#endif
            ;
    }
}