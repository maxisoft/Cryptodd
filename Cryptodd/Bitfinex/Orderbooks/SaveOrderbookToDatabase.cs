using System.Data;
using Cryptodd.Bitfinex.Models;
using Cryptodd.Databases;
using Cryptodd.Databases.Postgres;
using Cryptodd.Databases.Tables.Bitfinex;
using Cryptodd.Features;
using Lamar;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using PetaPoco.SqlKata;
using Serilog.Core;

namespace Cryptodd.Bitfinex.Orderbooks;

public class SaveOrderbookToDatabase : IOrderbookHandler
{
    private readonly IContainer _container;
    private readonly Logger _logger;

    public SaveOrderbookToDatabase(Logger logger, IContainer container)
    {
        _logger = logger;
        _container = container;
    }

    public bool Disabled { get; set; }

    public async Task Handle(IReadOnlyCollection<OrderbookEnvelope> orderbooks, CancellationToken cancellationToken)
    {
        if (!_container.GetInstance<IFeatureList>().HasPostgres())
        {
            return;
        }

        await using var container = _container.GetNestedContainer()!;
        await using var connection = container.GetInstance<NpgsqlConnection>();
        if (connection is not null)
        {
            await HandlePostgres(container, connection, orderbooks, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask HandlePostgres(INestedContainer container, NpgsqlConnection connection,
        IReadOnlyCollection<OrderbookEnvelope> orderbooks, CancellationToken cancellationToken)
    {
        Task Reconnect(NpgsqlConnection connection)
        {
            return connection.State != ConnectionState.Open
                ? connection.OpenAsync(cancellationToken)
                : Task.CompletedTask;
        }

        await Reconnect(connection).ConfigureAwait(false);

        var tableSchemaForSymbol = container.GetInstance<OrderbookTableSchemaForSymbolFactory>();
        var tableService = container.GetInstance<TableService>();
        await using var connectionPool = container.GetInstance<IMiniConnectionPool>();
        using var semaphore = new SemaphoreSlim(checked((int)connectionPool.CappedSize), checked((int)connectionPool.CappedSize));
        await Parallel.ForEachAsync(orderbooks, cancellationToken, async (orderbook, token) =>
        {
            var table = tableSchemaForSymbol.Resolve(orderbook.Symbol.ToLowerInvariant(), CompilerType.Postgres);
            await semaphore.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await using var rent = await connectionPool.RentAsync(token).ConfigureAwait(false);
                await tableService.CreateTableIfNotExists<OrderbookTableSchema>(table, rent.Connection, token)
                    .ConfigureAwait(false);
                
                await Reconnect(rent.Connection).ConfigureAwait(false);
                await using var tr =
                    await rent.Connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token).ConfigureAwait(false);
                await ImportOrderbook(rent.Connection, table, orderbook, token).ConfigureAwait(false);
                await tr.CommitAsync(token).ConfigureAwait(false);
            }
            finally
            {
                semaphore.Release();
            }
        });
    }

    private static async Task ImportOrderbook(NpgsqlConnection connection, OrderbookTableSchema table,
        OrderbookEnvelope orderbook, CancellationToken cancellationToken)
    {
        await using var writer =
            await connection.BeginBinaryImportAsync(
                $"COPY \"{table.Schema}\".\"{table.Table}\" FROM STDIN (FORMAT BINARY)", cancellationToken).ConfigureAwait(false);
        await writer.StartRowAsync(cancellationToken);
        await writer.WriteAsync(orderbook.Time, NpgsqlDbType.Bigint, cancellationToken);

        static void WriteTuples(OrderbookEnvelope orderbookEnvelope, NpgsqlBinaryImporter npgsqlBinaryImporter)
        {
            foreach (ref var ob in orderbookEnvelope.Orderbook)
            {
                // ReSharper disable MethodHasAsyncOverloadWithCancellation
                npgsqlBinaryImporter.Write((float)ob.Price, NpgsqlDbType.Real);
                // ReSharper disable once RedundantCast
                npgsqlBinaryImporter.Write((int)ob.Count, NpgsqlDbType.Integer);
                npgsqlBinaryImporter.Write((float)ob.Size, NpgsqlDbType.Real);
            }
        }

        WriteTuples(orderbook, writer);

        await writer.CompleteAsync(cancellationToken).ConfigureAwait(false);
    }
}