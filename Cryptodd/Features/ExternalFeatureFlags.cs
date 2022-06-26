namespace Cryptodd.Features;

[Flags]
public enum ExternalFeatureFlags
{
    None = 0,
    Postgres = 1 << 0,
    TimeScaleDb = 1 << 1,
    Sqlite = 1 << 2,
    Redis = 1 << 3
}