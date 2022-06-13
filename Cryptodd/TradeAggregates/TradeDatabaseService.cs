using System.Data;
using System.Reflection;
using Lamar;
using Npgsql;
using Serilog;
using SqlKata;
using SqlKata.Compilers;
using SqlKata.Execution;

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

    public async ValueTask<List<long>> GetLatestIds<TDbTransaction>(TDbTransaction transaction, string market,
        int limit,
        CancellationToken cancellationToken = default) where TDbTransaction : IDbTransaction
    {
        var tableName = TradeTableName(market);
        var query = new XQuery(transaction.Connection, _container.GetInstance<Compiler>())
            .From("ids")
            .With("ids",
                new Query($"ftx.{tableName}")
                    .Select("id")
                    .OrderByDesc("time")
                    .Limit(limit))
            .Select("id")
            .OrderBy("id");
        return (await query
            .GetAsync<long>(transaction, cancellationToken: cancellationToken)).ToList();
    }

    public ValueTask<long> GetLastTime<TDbTransaction>(TDbTransaction transaction, string name,
        bool allowTableCreation = true,
        CancellationToken cancellationToken = default) where TDbTransaction : IDbTransaction =>
        QueryTime(transaction, name, true, allowTableCreation, cancellationToken);

    public ValueTask<long> GetFirstTime<TDbTransaction>(TDbTransaction transaction, string name,
        CancellationToken cancellationToken,
        bool allowTableCreation = true) where TDbTransaction : IDbTransaction =>
        QueryTime(transaction, name, false, allowTableCreation, cancellationToken);

    public string EscapeMarket(string market) => market.Replace('/', '_').Replace('-', '_').Replace(' ', '_')
        .Replace('.', '_').ToLowerInvariant();

    public string TradeTableName(string market) =>
        "ftx_trade_template".Replace("_template", $"_{EscapeMarket(market)}");

    private async ValueTask<long> QueryTime<TDbTransaction>(TDbTransaction transaction, string name, bool max,
        bool allowTableCreation,
        CancellationToken cancellationToken) where TDbTransaction : IDbTransaction
    {
        if (transaction is not NpgsqlTransaction pgconnTransaction)
        {
            _logger.Error("Only postgres supported for now");
            return 0;
        }

        var tableName = TradeTableName(name);
        long maxTime;
        var spName = Guid.NewGuid();
        await pgconnTransaction.SaveAsync(spName.ToString(), cancellationToken).ConfigureAwait(false);
        try
        {
            maxTime = await new XQuery(pgconnTransaction.Connection, _container.GetInstance<Compiler>())
                .From($"ftx.{tableName}")
                .SelectRaw($"coalesce({(max ? "MAX" : "MIN")}(\"time\"), 0)")
                .Limit(1)
                .FirstOrDefaultAsync<long>(transaction, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
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

            if (pgconnTransaction.Connection is {State: ConnectionState.Open})
            {
                await pgconnTransaction.RollbackAsync(spName.ToString(), cancellationToken).ConfigureAwait(false);
            }
            else if (pgconnTransaction.Connection is not null)
            {
                await pgconnTransaction.Connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var container = _container.GetNestedContainer();
            await using var newConn = container.GetInstance<NpgsqlConnection>();
            
            if (newConn.State != ConnectionState.Open)
            {
                await newConn.OpenAsync(cancellationToken).ConfigureAwait(false);
            }
            await using var newTransaction = await newConn.BeginTransactionAsync(IsolationLevel.Serializable ,cancellationToken).ConfigureAwait(false);
            await CreateTradeTable(newTransaction, tableName, cancellationToken);

            maxTime = await QueryTime(newTransaction, name, max, false,
                cancellationToken).ConfigureAwait(false);
            await newTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return maxTime;
    }

    private async Task CreateTradeTable(NpgsqlTransaction transaction, string tableName,
        CancellationToken cancellationToken)
    {
        var query = await GetFileContents("ftx_trades.sql");
        query = query.Replace("ftx_trade_template", tableName);
        await using var cmd = new NpgsqlCommand(query, transaction.Connection, transaction);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static Task<string> GetFileContents(string sampleFile)
    {
        var asm = Assembly.GetExecutingAssembly();
        var resource = $"{asm.GetName().Name}.sql.postgres.{sampleFile}";
        using (var stream = asm.GetManifestResourceStream(resource))
        {
            if (stream is not null)
            {
                using var reader = new StreamReader(stream);
                return reader.ReadToEndAsync();
            }
        }

        return Task.FromResult(string.Empty);
    }
}