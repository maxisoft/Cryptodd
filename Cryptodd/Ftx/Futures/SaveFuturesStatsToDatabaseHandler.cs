using System.Collections.Immutable;
using System.Data;
using Cryptodd.Features;
using Cryptodd.Ftx.Models.DatabasePoco;
using Lamar;
using Maxisoft.Utils.Disposables;
using Npgsql;
using NpgsqlTypes;
using PetaPoco;
using PetaPoco.SqlKata;
using Serilog;
using SqlKata;

namespace Cryptodd.Ftx.Futures;

public class SaveFuturesStatsToDatabaseHandler : IFuturesStatsHandler
{
    private readonly ILogger _logger;
    private readonly IContainer _container;

    public SaveFuturesStatsToDatabaseHandler(IContainer container, ILogger logger)
    {
        _logger = logger.ForContext(GetType());
        _container = container;
    }

    public bool Disabled { get; set; }

    public async Task Handle(IReadOnlyCollection<FutureStats> futureStats, CancellationToken cancellationToken)
    {
        if (!_container.GetInstance<IFeatureList>().HasDatabase())
        {
            return;
        }
        await using var container = _container.GetNestedContainer();
        var databases = container.GetAllInstances<IDatabase>();
        using var dm = new DisposableManager(databases);
        
        foreach (var db in databases)
        {
            dm.LinkDisposableAsWeak(db);
            if (db.Connection is NpgsqlConnection pgconn)
            {
                await InsertPostgresFast(futureStats, db, pgconn, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // slow path for other database
                // TODO use SqlKata to build a multi insert query
                using var tr = db.GetTransaction();
                foreach (var stats in futureStats)
                {
                    await db.InsertAsync(cancellationToken, stats).ConfigureAwait(false);
                }
                tr.Complete();
            }
            
        }
    }

    private static async Task InsertPostgresFast(IEnumerable<FutureStats> futureStatsList, IDatabase db,
        NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var tr = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        var query = new Query(FutureStats.Naming.TableName).SelectRaw("COALESCE(MAX(id), 0)").Limit(1)
            .ToSql(CompilerType.Postgres);
        var lastId = await db.FirstOrDefaultAsync<long>(cancellationToken, query).ConfigureAwait(false);

        await using (var writer =
                     await connection.BeginBinaryImportAsync(
                         $"COPY \"{FutureStats.Naming.TableName}\" FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var futures in futureStatsList)
            {
                await writer.StartRowAsync(cancellationToken);
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

            await writer.CompleteAsync(cancellationToken).ConfigureAwait(false);
        }

        await db.ExecuteAsync(cancellationToken, @"SELECT setval('ftx_futures_stats_id_seq', @0, true);", lastId).ConfigureAwait(false);

        await tr.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}