using System.Data;
using Cryptodd.Features;
using Cryptodd.IoC;
using Cryptodd.TradeAggregates;
using Lamar;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Serilog;
using SqlKata.Execution;

namespace Cryptodd.Databases.Postgres;

public class TimescaleDBOptions
{
    public bool Enabled { get; set; } = true;
    public bool AllowInstallation { get; set; } = true;
    public long HyperTableCommandTimeout { get; set; } = (long)TimeSpan.FromHours(4).TotalMilliseconds;

    public bool EnableCompression { get; set; } = false;
}

public class TimescaleDB : IService
{
    private readonly IContainer _container;
    private readonly ILogger _logger;
    private readonly TimescaleDBOptions Options = new();

    public TimescaleDB(ILogger logger, IContainer container, IConfiguration configuration)
    {
        _logger = logger.ForContext(GetType());
        _container = container;
        configuration.GetSection("Postgres:TimescaleDB").Bind(Options);
    }

    internal async Task Setup(CancellationToken cancellationToken)
    {
        if (!Options.Enabled)
        {
            return;
        }

        if (!_container.GetInstance<IFeatureList>().HasPostgres())
        {
            return;
        }

        await using var container = _container.GetNestedContainer();
        var installed = await IsExtensionInstalled(cancellationToken);
        if (!installed && Options.AllowInstallation)
        {
            await InstallExt(cancellationToken);
        }

        await CreateTableSize2(cancellationToken);
        if (!installed)
        {
            return;
        }

        _container.GetInstance<IFeatureListRegistry>().RegisterFeature(ExternalFeatureFlags.TimeScaleDb);
        await CreateFtxFuturesStatsHyperTables(cancellationToken);
        await CreateFtxTradesHyperTables(cancellationToken);
        if (Options.EnableCompression)
        {
            await CompressFtxTradesHyperTables(cancellationToken);
            await using var conn = container.GetInstance<NpgsqlConnection>();
        }
    }

    internal async Task CreateFtxFuturesStatsHyperTables(CancellationToken cancellationToken, bool tableCreate = true)
    {
        var createQuery = await GetFileContents("ftx_futures_stats_hyper_tables.sql");
        await using var container = _container.GetNestedContainer();
        using var db = container.GetInstance<QueryFactory>();
        await using var conn = (NpgsqlConnection)db.Connection;
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(cancellationToken);
        }

        var exists = await db.Query("table_sizes2")
            //.Where("schema", "=", "ftx")
            .SelectRaw("COALESCE(num_chunks, -1)")
            .WhereLike("name", "ftx_futures_stats")
            .Limit(1)
            .FirstOrDefaultAsync<long>(cancellationToken: cancellationToken);

        if (exists == 0)
        {
            if (tableCreate)
            {
                var createTable = await GetFileContents("ftx_futures_stats_postgres.sql", "");
                await using var command = new NpgsqlCommand(createTable, conn);
                await command.ExecuteNonQueryAsync(cancellationToken);

                // ReSharper disable once TailRecursiveCall
                await CreateFtxFuturesStatsHyperTables(cancellationToken, false);
            }
            
            return;
        }

        if (exists == -1)
        {
            await using var command = new NpgsqlCommand(createQuery, conn);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    internal async Task CreateFtxTradesHyperTables(CancellationToken cancellationToken)
    {
        var createQueryTemplate = await GetFileContents("trades_hyper_tables.sql");
        await using var container = _container.GetNestedContainer();

        using var db = container.GetInstance<QueryFactory>();
        await using var conn = (NpgsqlConnection)db.Connection;
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(cancellationToken);
        }

        var tableSizes = await db.Query("table_sizes2")
            .Where("schema", "=", "ftx")
            .WhereLike("name", "ftx_trade_%_%")
            .OrderByRaw("COALESCE(num_chunks, -1)")
            .OrderByRaw("pg_size_bytes(total_bytes) DESC")
            .GetAsync(cancellationToken: cancellationToken);

        foreach (var tableSize in tableSizes)
        {
            if (tableSize is null)
            {
                continue;
            }

            if (tableSize.num_chunks >= 0)
            {
                continue;
            }

            if (tableSize.row_estimate <= 0)
            {
                continue;
            }

            var toHyperTableQuery = createQueryTemplate.Replace("ftx_trade_btc_usd", (string)tableSize.name);
            await using var command = new NpgsqlCommand(toHyperTableQuery, conn)
                { CommandTimeout = checked((int)(Options.HyperTableCommandTimeout / 1000)) };
            _logger.Information("Converting table {Schema}.{Table} {Size} into a hyper table ...", tableSize.schema,
                tableSize.name, tableSize.total_bytes);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    internal async Task CompressFtxTradesHyperTables(CancellationToken cancellationToken)
    {
        await using var container = _container.GetNestedContainer();

        using var db = container.GetInstance<QueryFactory>();
        await using var conn = (NpgsqlConnection)db.Connection;
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(cancellationToken);
        }

        var tableSizes = await db.Query("table_sizes2")
            .Where("schema", "=", "ftx")
            .WhereLike("name", "ftx_trade_%_%")
            .WhereNotLike("name", "ftx_trade_agg_%_%")
            .OrderByRaw("COALESCE(num_chunks, -1)")
            .OrderByRaw("pg_size_bytes(total_bytes) DESC")
            .GetAsync(cancellationToken: cancellationToken);

        foreach (var tableSize in tableSizes)
        {
            if (tableSize is null)
            {
                continue;
            }

            if (tableSize.num_chunks is null || tableSize.num_chunks < 0)
            {
                continue;
            }

            var maxTime = await db.Query($"ftx.{tableSize.name}")
                .SelectRaw("COALESCE(MAX(time), 0)")
                .Limit(1)
                .FirstOrDefaultAsync<long>(cancellationToken: cancellationToken);

            var allowCompression = true;
            if (maxTime <= 0 || (DateTimeOffset.FromUnixTimeMilliseconds(maxTime) - DateTimeOffset.UtcNow).Duration() >=
                TimeSpan.FromDays(1))
            {
                _logger.Verbose("Skipping {Table} compression as it's not up to date", tableSize.name);
                allowCompression = false;
            }

            if (allowCompression && tableSize.compression_enabled is not true)
            {
                await using var enableCompressionCommand =
                    new NpgsqlCommand($"ALTER TABLE ftx.\"{tableSize.name}\" SET (timescaledb.compress)", conn);
                await enableCompressionCommand.ExecuteNonQueryAsync(cancellationToken);
                _logger.Debug("Enabling Compression on table {Schema}.{Table} {Size} ...", tableSize.schema,
                    tableSize.name,
                    tableSize.total_bytes);
            }

            var older_than = maxTime - (long)TimeSpan.FromDays(100).TotalMilliseconds;

            if (allowCompression)
            {
                await using var compresscommand = new NpgsqlCommand(@$"
SELECT compress_chunk(c)
FROM show_chunks('ftx.""{tableSize.name}""', older_than => '{older_than}'::bigint ) as c
WHERE c::text NOT IN (SELECT chunk_schema || '.' || chunk_name FROM chunk_compression_stats('ftx.""{tableSize.name}""') WHERE compression_status = 'Compressed')
AND c NOT IN (SELECT show_chunks('ftx.""{tableSize.name}""', newer_than => '{older_than}'::bigint ))
ORDER BY c",
                    conn) { CommandTimeout = (int)(Options.HyperTableCommandTimeout / 1000) };
                _logger.Debug("Compressing table {Schema}.{Table} {Size} ...", tableSize.schema, tableSize.name,
                    tableSize.total_bytes);
                await compresscommand.ExecuteNonQueryAsync(cancellationToken);
            }

            if (older_than > 0)
            {
                await using var decompresscommand = new NpgsqlCommand(@$"
SELECT decompress_chunk(c)
FROM show_chunks('ftx.""{tableSize.name}""', newer_than => '{older_than}'::bigint ) as c
WHERE c::text IN (SELECT chunk_schema || '.' || chunk_name FROM chunk_compression_stats('ftx.""{tableSize.name}""') WHERE compression_status = 'Compressed')
ORDER BY c DESC",
                    conn) { CommandTimeout = (int)(Options.HyperTableCommandTimeout / 1000) };
                _logger.Debug("Decompressing table {Schema}.{Table} ...", tableSize.schema, tableSize.name);
                await decompresscommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    internal async Task CreateTableSize2(CancellationToken cancellationToken)
    {
        var createQuery = await GetFileContents("tablesize2.sql", "");
        await using var container = _container.GetNestedContainer();
        await using var conn = container.GetInstance<NpgsqlConnection>();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(cancellationToken);
        }

        await using var command = new NpgsqlCommand(createQuery, conn);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await using var ftxSchema = new NpgsqlCommand("CREATE SCHEMA IF NOT EXISTS \"ftx\"", conn);
        await ftxSchema.ExecuteNonQueryAsync(cancellationToken);
        await using var bitfinexSchema = new NpgsqlCommand("CREATE SCHEMA IF NOT EXISTS \"bitfinex\"", conn);
        await bitfinexSchema.ExecuteNonQueryAsync(cancellationToken);
    }

    internal async Task InstallExt(CancellationToken cancellationToken)
    {
        await using var container = _container.GetNestedContainer();
        await using var conn = container.GetInstance<NpgsqlConnection>();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(cancellationToken);
        }

        await using var command = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS timescaledb;", conn);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<bool> IsExtensionInstalled(CancellationToken cancellationToken)
    {
        if (!Options.Enabled)
        {
            return false;
        }

        await using var container = _container.GetNestedContainer();
        await using var conn = container.GetInstance<NpgsqlConnection>();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(cancellationToken);
        }

        await using var command =
            new NpgsqlCommand("SELECT extversion FROM pg_extension WHERE extname ILIKE 'timescaledb%';", conn);

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return false;
        }

        var version = reader.GetString(0);
        _logger.Debug("timescaledb extension Version {Version}", version);
        return true;
    }

    internal static Task<string> GetFileContents(string sampleFile, string prefix = "timescaledb.") =>
        TradeDatabaseService.GetFileContents($"{prefix}{sampleFile}");
}