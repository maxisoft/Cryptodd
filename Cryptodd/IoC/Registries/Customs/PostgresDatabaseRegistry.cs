using System.Data;
using System.Data.Common;
using Lamar;
using Maxisoft.Utils.Disposables;
using Maxisoft.Utils.Empties;
using Maxisoft.Utils.Objects;
using Microsoft.Extensions.Configuration;
using Npgsql;
using PetaPoco;
using PetaPoco.Providers;
using Serilog;
using Serilog.Core;
using SqlKata.Compilers;
using SqlKata.Execution;

namespace Cryptodd.IoC.Registries.Customs;

public class PostgresDatabaseRegistry : ServiceRegistry
{
    public const string DefaultConnectionString =
        @"Host=127.0.0.1;Username=cryptodduser;Database=cryptodd;Port=5432";

    private readonly DisposableManager _disposableManager = new();

    private readonly object _lockObject = new();
    private ILogger _logger = Logger.None;
    private Lazy<PostgresCompiler> compiler = new Lazy<PostgresCompiler>(() => new PostgresCompiler());

    public PostgresDatabaseRegistry()
    {
        static string GetConnectionString(IConfiguration configuration)
        {
            var postgresSection = configuration.GetSection("Postgres");
            return postgresSection.GetValue<string>("ConnectionString", DefaultConnectionString);
        }

        For<IDatabase>().Add(context =>
        {
            _logger.Warning("Creating a new Poco Postgres database object => memory leak");
            return DatabaseConfiguration.Build()
                .UsingConnectionString(GetConnectionString(context.GetInstance<IConfiguration>()))
                .UsingProvider<PostgreSQLDatabaseProvider>().Create();
        });

        For<NpgsqlConnection>().Use(static context =>
            new NpgsqlConnection(GetConnectionString(context.GetInstance<IConfiguration>())));

        For<PostgresCompiler>().Use(_ => compiler.Value);
        For<Compiler>().Add(_ => compiler.Value);

        For<DbConnection>().Add(static context => context.GetInstance<NpgsqlConnection>());
        For<IDbConnection>().Add(static context => context.GetInstance<DbConnection>());

        For<QueryFactory>().Add(context =>
            new QueryFactory(context.GetInstance<NpgsqlConnection>(), compiler.Value)
        );

        For<NpgsqlTransaction>().Use(context => context.GetInstance<NpgsqlConnection>().BeginTransaction());
    }
}