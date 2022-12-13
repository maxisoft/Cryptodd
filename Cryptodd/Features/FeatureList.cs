namespace Cryptodd.Features;

public interface IFeatureList
{
    bool HasPostgres();
    bool HasTimescaleDb();
    bool HasRedis();

    bool HasDatabase() => HasPostgres();
}

public interface IFeatureListRegistry
{
    void RegisterFeature(ExternalFeatureFlags feature);
}

public class FeatureList : IFeatureList, IFeatureListRegistry
{
    private ExternalFeatureFlags _features = ExternalFeatureFlags.None;

    public bool HasPostgres() => _features.HasFlag(ExternalFeatureFlags.Postgres);
    public bool HasTimescaleDb() => _features.HasFlag(ExternalFeatureFlags.TimeScaleDb) && HasPostgres();
    public bool HasRedis() => _features.HasFlag(ExternalFeatureFlags.Redis);

    public void RegisterFeature(ExternalFeatureFlags feature)
    {
        _features |= feature;
    }
}