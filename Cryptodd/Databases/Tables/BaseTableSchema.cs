using Cryptodd.FileSystem;
using PetaPoco.SqlKata;

namespace Cryptodd.Databases.Tables;

public abstract class BaseTableSchema : TableSchema
{
    private readonly IResourceResolver _resourceResolver;

    public BaseTableSchema(IResourceResolver resourceResolver)
    {
        _resourceResolver = resourceResolver;
    }

    protected internal string ResourceName { get; set; } = string.Empty;

    public override async ValueTask<string> CreateQuery(CompilerType compilerType, CancellationToken cancellationToken)
    {
        var resourceName = ResourceName;
        switch (compilerType)
        {
            case CompilerType.Postgres:
                if (string.IsNullOrEmpty(resourceName))
                {
                    resourceName = Table;
                }

                if (!resourceName.Contains('.'))
                {
                    resourceName = $"sql.postgres.{resourceName}";
                }

                if (!resourceName.EndsWith(".sql", StringComparison.InvariantCultureIgnoreCase))
                {
                    resourceName = $"{resourceName}.sql";
                }

                return await _resourceResolver.GetResource(resourceName, cancellationToken);
            default:
                throw new ArgumentOutOfRangeException(nameof(compilerType), compilerType, null);
        }
    }

    public override ValueTask<string> ExistsQuery(CompilerType compilerType, CancellationToken cancellationToken)
    {
        switch (compilerType)
        {
            case CompilerType.Postgres:
                var regclass = Table;
                if (!string.IsNullOrEmpty(Schema))
                {
                    regclass = $"{Schema}.{regclass}";
                }

                return ValueTask.FromResult<string>($"SELECT to_regclass('{regclass}') IS NOT NULL AS ex;");
            default:
                throw new ArgumentOutOfRangeException(nameof(compilerType), compilerType, null);
        }
    }
}