namespace Cryptodd;

public static class Const
{
    public const string ApplicationName = "cryptodd";
    public const string EnvPrefix = "CRYPTODD_";
    public const string EnvPrefixShort = "CDD_";
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