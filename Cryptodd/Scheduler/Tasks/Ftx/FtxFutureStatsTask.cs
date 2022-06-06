using System.Collections.Immutable;
using System.Diagnostics;
using Cryptodd.Ftx;
using Cryptodd.Ftx.Models.DatabasePoco;
using Cryptodd.Pairs;
using Lamar;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;
using PetaPoco;
using PetaPoco.SqlKata;
using Serilog;
using SqlKata;

namespace Cryptodd.Scheduler.Tasks.Ftx;

public class FtxFutureStatsTask : BasePeriodicScheduledTask
{
    private readonly IPairFilterLoader _pairFilterLoader;

    public FtxFutureStatsTask(ILogger logger, IConfiguration configuration, IContainer container,
        IPairFilterLoader pairFilterLoader) : base(logger,
        configuration, container)
    {
        Period = TimeSpan.FromMinutes(1);
        NextSchedule = DateTimeOffset.Now;
        _pairFilterLoader = pairFilterLoader;
    }

    public override IConfigurationSection Section =>
        Configuration.GetSection("Ftx").GetSection("FutureStats").GetSection("Task");


    public override async Task Execute(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var http = Container.GetInstance<IFtxPublicHttpApi>();
        var futures = await http.GetAllFuturesAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var pairFilter = await _pairFilterLoader.GetPairFilterAsync("Ftx.FutureStats", cancellationToken)
            .ConfigureAwait(false);
        var pocos = futures
            .Where(future => pairFilter.Match(future.Name))
            .Select(future =>
            {
                var spread = 0f;
                if (future.Bid.GetValueOrDefault() > 0)
                {
                    spread = (float)(future.Ask.GetValueOrDefault() / future.Bid.GetValueOrDefault() - 1);
                }

                return new FutureStats
                {
                    Spread = spread,
                    Time = now,
                    MarketHash = PairHasher.Hash(future.Name),
                    OpenInterest = future.OpenInterest.GetValueOrDefault(),
                    OpenInterestUsd = future.OpenInterestUsd.GetValueOrDefault(),
                    Mark = (float)(future.Mark ?? future.Ask ?? future.Bid).GetValueOrDefault()
                };
            }).ToImmutableArray();

        var databases = Container.GetAllInstances<IDatabase>();

        foreach (var db in databases)
        {
            using var tr = db.GetTransaction();
            if (db.Connection is NpgsqlConnection pgconn)
            {
                await InsertPostgresFast(pocos, db, pgconn, cancellationToken);
            }
            else
            {
                foreach (var poco in pocos)
                {
                    await db.InsertAsync(cancellationToken, poco);
                }
            }

            tr.Complete();
        }

        Logger.Debug("Inserted {Count} FutureStats in {Elapsed}", pocos.Length, sw.Elapsed);
    }

    private static async Task InsertPostgresFast(ImmutableArray<FutureStats> futureStatsList, IDatabase db,
        NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        using var tr = db.GetTransaction();
        await db.ExecuteAsync(cancellationToken,
            $"LOCK TABLE \"{FutureStats.Naming.TableName}\" IN SHARE ROW EXCLUSIVE MODE NOWAIT;");

        var query = new Query(FutureStats.Naming.TableName).SelectRaw("COALESCE(MAX(id), 0)").Limit(1)
            .ToSql(CompilerType.Postgres);
        var lastId = await db.FirstOrDefaultAsync<long>(cancellationToken, query);

        await using (var writer =
                     await connection.BeginBinaryImportAsync(
                         $"COPY \"{FutureStats.Naming.TableName}\" FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var futures in futureStatsList)
            {
                await writer.StartRowAsync(cancellationToken).ConfigureAwait(false);
#pragma warning disable CA2016
                // ReSharper disable MethodSupportsCancellation
                await writer.WriteAsync(++lastId, NpgsqlDbType.Bigint);
                await writer.WriteAsync(futures.MarketHash, NpgsqlDbType.Bigint);
                await writer.WriteAsync(futures.Time, NpgsqlDbType.Bigint);
                await writer.WriteAsync(futures.OpenInterest, NpgsqlDbType.Double);
                await writer.WriteAsync(futures.OpenInterestUsd, NpgsqlDbType.Double);
                await writer.WriteAsync(futures.NextFundingRate, NpgsqlDbType.Real);
                await writer.WriteAsync(futures.Spread, NpgsqlDbType.Real);
                await writer.WriteAsync(futures.Mark, NpgsqlDbType.Real);
                // ReSharper restore MethodSupportsCancellation
#pragma warning restore CA2016
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await db.ExecuteAsync(cancellationToken, @"SELECT setval('ftx_futures_stats_id_seq', @0, true);", lastId + 1);

        tr.Complete();
    }
}