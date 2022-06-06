using Lamar;
using Microsoft.Extensions.Configuration;
using PetaPoco;
using PetaPoco.Providers;

namespace Cryptodd.IoC.Registries.Customs;

public class PostgresDatabaseRegistry: ServiceRegistry
{
    public const string DefaultConnectionString =
        @"Host=127.0.0.1;Username=cryptodduser;Password=cryptouserpass;Database=cryptodd;Port=5432";
    
    public PostgresDatabaseRegistry()
    {
        ForSingletonOf<IDatabase>().Add(context =>
        {
            var configuration = context.GetInstance<IConfiguration>();
            var postgresSection = configuration.GetSection("Postgres");
            var res = DatabaseConfiguration.Build()
                .UsingConnectionString(postgresSection.GetValue<string>("ConnectionString", DefaultConnectionString))
                .UsingProvider<PostgreSQLDatabaseProvider>()
                .Create();
            return res;
        });
    }
}