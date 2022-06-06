using Lamar;
using Microsoft.Extensions.Configuration;
using PetaPoco;
using PetaPoco.Providers;

namespace Cryptodd.IoC.Registries.Customs;

public class SqliteDatabaseRegistry: ServiceRegistry
{
    public SqliteDatabaseRegistry()
    {
        ForSingletonOf<IDatabase>().Add(context =>
        {
            var configuration = context.GetInstance<IConfiguration>();
            var postgresSection = configuration.GetSection("Postgres");
            var basePath = configuration.GetValue<string>("BasePath");
            var defaultConnectionString = $"Data Source={Path.Combine(basePath, "cryptodd.db")};";
            var res = DatabaseConfiguration.Build()
                .UsingConnectionString(postgresSection.GetValue<string>("ConnectionString", defaultConnectionString))
                .UsingProvider<SQLiteDatabaseProvider>()
                .Create();
            return res;
        });
    }
}