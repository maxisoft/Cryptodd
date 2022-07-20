using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using Cryptodd.Databases.Tables;
using Cryptodd.IoC;
using Dapper;
using Lamar;
using Npgsql;
using PetaPoco.SqlKata;
using Serilog.Core;

namespace Cryptodd.Databases;

[Singleton]
public class TableService : IService
{
    private readonly Logger _logger;
    private readonly ConcurrentDictionary<(CompilerType, TableSchema), bool> _existCache = new();

    public TableService(Logger logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> Exists<TTableSchema>(TableSchema table, NpgsqlConnection connection,
        CancellationToken cancellationToken) where TTableSchema : TableSchema
    {
        var existsQuery = await table.ExistsQuery(CompilerType.Postgres, cancellationToken);
        _logger.Verbose("using query {Query} to check existence", existsQuery);
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        var row = await connection.QueryFirstOrDefaultAsync<bool>(existsQuery, cancellationToken).ConfigureAwait(false);
        return row;
    }

    public async ValueTask<bool> CreateTableIfNotExists<TTableSchema>(TableSchema table, NpgsqlConnection connection,
        CancellationToken cancellationToken) where TTableSchema : TableSchema
    {
        if (_existCache.TryGetValue((CompilerType.Postgres, table), out var value) && value)
        {
            return false;
        }

        value = await Exists<TTableSchema>(table, connection, cancellationToken).ConfigureAwait(false);
        if (value)
        {
            _existCache.TryAdd((CompilerType.Postgres, table), value);

            return false;
        }

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        var createQuery = await table.CreateQuery(CompilerType.Postgres, cancellationToken);
        _logger.Information("creating a table for {Schema}.{Table}", table.Schema, table.Table);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(createQuery, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        value = await Exists<TTableSchema>(table, connection, cancellationToken).ConfigureAwait(false);
        Debug.Assert(value);
        return value;
    }
}