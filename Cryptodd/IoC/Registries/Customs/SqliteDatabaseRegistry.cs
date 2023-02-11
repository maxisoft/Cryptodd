using Cryptodd.IO.FileSystem;
using Lamar;
using Microsoft.Extensions.Configuration;
using PetaPoco;
using PetaPoco.Providers;

namespace Cryptodd.IoC.Registries.Customs;

public class SqliteDatabaseRegistry: ServiceRegistry
{
    public const string DatabaseFileName = "cryptodd.sqlite";
    public const string FileType = "sqlite3";
    public SqliteDatabaseRegistry()
    {
        For<IDatabase>().Add(context =>
        {
            var configuration = context.GetInstance<IConfiguration>();
            var postgresSection = configuration.GetSection("Postgres");
            var basePath = configuration.GetValue<string>("BasePath");
            var pathResolver = context.GetInstance<IPathResolver>();
            var dbPath = pathResolver.Resolve(DatabaseFileName,
                new ResolveOption
                {
                    Namespace = GetType().Namespace!, FileType = FileType,
                    IntendedAction = FileIntendedAction.Append | FileIntendedAction.Read | FileIntendedAction.Create |
                                     FileIntendedAction.Write
                });
            var defaultConnectionString = $"Data Source={dbPath};";
            var res = DatabaseConfiguration.Build()
                .UsingConnectionString(postgresSection.GetValue<string>("ConnectionString", defaultConnectionString))
                .UsingProvider<SQLiteDatabaseProvider>()
                .Create();
            return res;
        });
    }
}