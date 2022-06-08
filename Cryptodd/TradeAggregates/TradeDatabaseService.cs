using System.Reflection;
using Cryptodd.IoC;
using Cryptodd.Pairs;
using Lamar;
using Npgsql;
using PetaPoco;
using PetaPoco.SqlKata;
using Serilog;
using SqlKata;

namespace Cryptodd.TradeAggregates;

public class TradeDatabaseService : ITradeDatabaseService
{
    internal const string TableDoesNotExistsSqlState = "42P01";

    private readonly IContainer _container;
    private readonly ILogger _logger;

    public TradeDatabaseService(IContainer container, ILogger logger)
    {
        _container = container;
        _logger = logger.ForContext(GetType());
    }

    public async ValueTask<List<long>> GetLatestIds(string market, int limit, IDatabase? database,
        CancellationToken cancellationToken = default)
    {
        var tableName = TradeTableName(market);
        if (database is not null)
        {
            return await QueryLatestIds(tableName, limit, database, cancellationToken).ConfigureAwait(false);
        }

        using (database = _container.GetInstance<IDatabase>())
        {
            using var tr = database.GetTransaction();
            return await QueryLatestIds(tableName, limit, database, cancellationToken);
        }
    }

    private async Task<List<long>> QueryLatestIds(string tableName, int limit, IDatabase database,
        CancellationToken cancellationToken)
    {
        var query = new Query("ids")
            .With("ids",
                new Query($"ftx.{tableName}")
                    .Select("id")
                    .OrderByDesc("time")
                    .Limit(limit))
            .Select("id")
            .OrderBy("id")
            .ToSql(CompilerType.Postgres);
        return await database.FetchAsync<long>(cancellationToken, query);
    }

    public async ValueTask<long> GetSavedTime(string name, CancellationToken cancellationToken,
        bool allowTableCreation = true)
    {
        using var database = _container.GetInstance<IDatabase>();
        using var tr = database.GetTransaction();
        if (database.Connection is not NpgsqlConnection pgconn)
        {
            _logger.Error("Only postgres supported for now");
            return 0;
        }

        var tableName = TradeTableName(name);
        var query = new Query($"ftx.{tableName}").SelectRaw("coalesce(MAX(\"time\"), 0)").Limit(1)
            .ToSql(CompilerType.Postgres);
        long maxTime;
        try
        {
            maxTime = await database.SingleOrDefaultAsync<long>(cancellationToken, query).ConfigureAwait(false);
        }
        catch (PostgresException e)
        {
            if (!allowTableCreation)
            {
                throw;
            }

            if (e.SqlState != TableDoesNotExistsSqlState)
            {
                throw;
            }

            await using (var newConn = pgconn.CloneWith(pgconn.ConnectionString))
            {
                tr.Complete();
                await newConn.OpenAsync(cancellationToken);
                try
                {
                    await CreateTradeTable(tableName, newConn, cancellationToken);
                }
                finally
                {
                    await newConn.CloseAsync();
                }
            }

            return await GetSavedTime(name, cancellationToken, allowTableCreation: false);
        }

        return maxTime;
    }

    private async Task CreateTradeTable(string tableName, NpgsqlConnection pgconn, CancellationToken cancellationToken)
    {
        var query = await GetFileContents("ftx_trades.sql");
        query = query.Replace("ftx_trade_template", tableName);
        await using var cmd = new NpgsqlCommand(query, pgconn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static Task<string> GetFileContents(string sampleFile)
    {
        var asm = Assembly.GetExecutingAssembly();
        var resource = $"{asm.GetName().Name}.sql.postgres.{sampleFile}";
        using (var stream = asm.GetManifestResourceStream(resource))
        {
            if (stream is not null)
            {
                var reader = new StreamReader(stream);
                return reader.ReadToEndAsync();
            }
        }

        return Task.FromResult(string.Empty);
    }
    
    public string EscapeMarket(string market) => market.Replace('/', '_').Replace('-', '_').Replace(' ', '_')
        .Replace('.', '_').ToLowerInvariant();

    public string TradeTableName(string market) => "ftx_trade_template".Replace("_template", $"_{EscapeMarket(market)}");
}