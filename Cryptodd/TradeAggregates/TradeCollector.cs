using System.Collections.Immutable;
using System.Reflection;
using Cryptodd.Ftx;
using Cryptodd.Ftx.Models.DatabasePoco;
using Cryptodd.IoC;
using Cryptodd.Pairs;
using Lamar;
using Npgsql;
using NpgsqlTypes;
using PetaPoco;
using PetaPoco.SqlKata;
using Serilog;
using SqlKata;

namespace Cryptodd.TradeAggregates;

public class TradeCollector : IService
{
    private const string TableDoesNotExistsSqlState = "42P01";
    private readonly IContainer _container;
    private readonly ILogger _logger;
    private readonly IPairFilterLoader _pairFilterLoader;

    public TradeCollector(IContainer container, ILogger logger, IPairFilterLoader pairFilterLoader)
    {
        _container = container;
        _logger = logger.ForContext(GetType());
        _pairFilterLoader = pairFilterLoader;
    }

    public async Task Collect(CancellationToken cancellationToken)
    {
        var http = _container.GetInstance<IFtxPublicHttpApi>();
        var pairFilter = await _pairFilterLoader.GetPairFilterAsync("Ftx.TradeCollector", cancellationToken)
            .ConfigureAwait(false);
        using var markets = await http.GetAllMarketsAsync(cancellationToken);

        var marketNames = markets.Select(market => market.Name).Where(name => pairFilter.Match(name)).ToImmutableList();

        foreach (var marketName in marketNames)
        {
            var savedTime = await GetSavedTime(marketName, cancellationToken).ConfigureAwait(false);
            await DownloadAndInsert(http, marketName, savedTime, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DownloadAndInsert(IFtxPublicHttpApi api, string marketName, long prevTime,
        CancellationToken cancellationToken)
    {
        bool wasZero = prevTime == 0;
        if (wasZero)
        {
            prevTime = (await new FtxTradeRemoteStartTimeSearch(api, marketName)
                    { MinimumDate = DateTimeOffset.UnixEpoch }.Search(cancellationToken).ConfigureAwait(false))
                .ToUnixTimeMilliseconds();
        }

        using var trades = await api.GetTradesAsync(marketName, prevTime / 1000 - (wasZero ? 0 : 1),
            (long)(prevTime / 1000.0 + TimeSpan.FromHours(12).TotalSeconds),
            cancellationToken).ConfigureAwait(false);
        trades.AsSpan().Sort(static (left, right) =>
        {
            var cmp = left.Time.CompareTo(right.Time);
            return cmp == 0 ? left.Id.CompareTo(right.Id) : cmp;
        });

        using var db = _container.GetInstance<IDatabase>();
        using var tr = db.GetTransaction();
        var tableName = TradeTableName("ftx_trade_template".Replace("_template", $"_{marketName}"));
        var query = new Query("ids")
            .With("ids",
                new Query($"ftx.{tableName}")
                    .Select("id")
                    .OrderByDesc("time")
                    .Limit(trades.Count))
            .Select("id")
            .OrderBy("id")
            .ToSql(CompilerType.Postgres);
        var prevIds = await db.FetchAsync<long>(cancellationToken, query);

        await db.ExecuteAsync(cancellationToken,
            $"LOCK TABLE ftx.\"{tableName}\" IN SHARE ROW EXCLUSIVE MODE NOWAIT;");

        query = new Query($"ftx.{tableName}").SelectRaw("COALESCE(MAX(\"time\"), 0)").Limit(1)
            .ToSql(CompilerType.Postgres);
        var lastTime = await db.FirstOrDefaultAsync<long>(cancellationToken, query);
        if (lastTime != 0 && lastTime != prevTime)
        {
            _logger.Warning("Concurrent write on table {Table} detected", tableName);
            return;
        }

        var maxId = prevIds.LastOrDefault();
        var connection = (NpgsqlConnection)db.Connection;
        await using (var writer =
                     await connection.BeginBinaryImportAsync(
                         $"COPY ftx.\"{tableName}\" FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            for (var index = 0; index < trades.Count; index++)
            {
                var trade = trades[index];
                if (prevIds.BinarySearch(trade.Id) >= 0)
                {
                    continue;
                }

                await writer.StartRowAsync(cancellationToken).ConfigureAwait(false);
#pragma warning disable CA2016
                // ReSharper disable MethodSupportsCancellation RedundantCast
                maxId = Math.Max(maxId, trade.Id);
                await writer.WriteAsync((long)trade.Id, NpgsqlDbType.Bigint);
                await writer.WriteAsync((long)trade.Time.ToUnixTimeMilliseconds(), NpgsqlDbType.Bigint);
                await writer.WriteAsync((float)trade.Price, NpgsqlDbType.Real);
                await writer.WriteAsync((float)trade.Size, NpgsqlDbType.Real);
                await writer.WriteAsync((short)trade.Flag, NpgsqlDbType.Smallint);
                // ReSharper restore MethodSupportsCancellation RedundantCast
#pragma warning restore CA2016
            }

            await writer.CompleteAsync(cancellationToken);
        }

        if (maxId > 0)
        {
            await db.ExecuteAsync(cancellationToken, $"SELECT setval('ftx.\"{tableName}_id_seq\"', @0, true);", maxId);
        }

        tr.Complete();
    }

    private async Task<long> GetSavedTime(string name, CancellationToken cancellationToken,
        bool allowTableCreation = true)
    {
        using var database = _container.GetInstance<IDatabase>();
        using var tr = database.GetTransaction();
        if (database.Connection is not NpgsqlConnection pgconn)
        {
            _logger.Error("Only postgres supported for now");
            return 0;
        }

        var tableName = TradeTableName("ftx_trade_template".Replace("_template", $"_{name}"));
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

    private static string TradeTableName(string market) => market.Replace('/', '_').Replace('-', '_').Replace(' ', '_')
        .Replace('.', '_').ToLowerInvariant();
}