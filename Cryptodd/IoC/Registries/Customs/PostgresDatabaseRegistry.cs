using Lamar;
using Maxisoft.Utils.Disposables;
using Maxisoft.Utils.Empties;
using Maxisoft.Utils.Objects;
using Microsoft.Extensions.Configuration;
using PetaPoco;
using PetaPoco.Providers;
using Serilog;
using Serilog.Core;

namespace Cryptodd.IoC.Registries.Customs;

public class PostgresDatabaseRegistry : ServiceRegistry
{
    public const string DefaultConnectionString =
        @"Host=127.0.0.1;Username=cryptodduser;Database=cryptodd;Port=5432";

    private readonly DisposableManager _disposableManager = new();

    private readonly object _lockObject = new();
    private ILogger _logger = Logger.None;

    public PostgresDatabaseRegistry()
    {
        For<IDatabase>().Add(context =>
        {
            var database = Database;
            if (database is not null)
            {
                return database;
            }

            lock (_lockObject)
            {
                database = Database;
                if (database is not null)
                {
                    return database;
                }

                _logger = context.GetInstance<ILogger>().ForContext(GetType());
                var configuration = context.GetInstance<IConfiguration>();
                RegisterChangeCallback(configuration);
                var postgresSection = configuration.GetSection("Postgres");
                _logger.Verbose("Creating a new Postgres database object");
                Database = DatabaseConfiguration.Build()
                    .UsingConnectionString(
                        postgresSection.GetValue<string>("ConnectionString", DefaultConnectionString))
                    .UsingProvider<PostgreSQLDatabaseProvider>()
                    .Create();
                _disposableManager.LinkDisposableAsWeak(Database);
                return Database;
            }
        });
    }

    internal IDatabase? Database { get; set; }

    private void RegisterChangeCallback(IConfiguration configuration)
    {
        Boxed<IDisposable> disposable = new(new EmptyDisposable());
        disposable.Ref() = configuration.GetReloadToken().RegisterChangeCallback(disposable =>
        {
            _logger.Debug("Reloading database configurations ...");
            Database = null;
            if (disposable is not Boxed<IDisposable> d || d.IsNull())
            {
                return;
            }

            Task.Factory.StartNew(static d =>
            {
                if (d is not Boxed<IDisposable> dd || dd.IsNull())
                {
                    return;
                }

                dd.Value.Dispose();
                dd.Value = new EmptyDisposable();
            }, d);
        }, disposable);
    }
}