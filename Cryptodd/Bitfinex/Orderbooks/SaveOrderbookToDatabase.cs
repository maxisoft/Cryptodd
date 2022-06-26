using System.Data;
using Cryptodd.Bitfinex.Models;
using Cryptodd.Databases;
using Cryptodd.Databases.Tables.Bitfinex;
using Lamar;
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
        foreach (var orderbook in orderbooks)
        {
            var table = tableSchemaForSymbol.Resolve(orderbook.Symbol.ToLowerInvariant(), CompilerType.Postgres);
            await tableService.CreateTableIfNotExists<OrderbookTableSchema>(table, connection, cancellationToken)
                .ConfigureAwait(false);
            await Reconnect(connection).ConfigureAwait(false);

            await using var writer =
                await connection.BeginBinaryImportAsync(
                    $"COPY {table.Schema}.\"{table.Table}\" FROM STDIN (FORMAT BINARY)", cancellationToken);
            await writer.StartRowAsync(cancellationToken);
            await writer.WriteAsync(orderbook.Time, NpgsqlDbType.Bigint, cancellationToken);
            for (var index = 0; index < orderbook.Orderbook.Count; index++)
            {
                var ob = orderbook.Orderbook[index];
                // ReSharper disable MethodHasAsyncOverloadWithCancellation
                writer.Write((float)ob.Price, NpgsqlDbType.Real);
                writer.Write(ob.Count, NpgsqlDbType.Integer);
                writer.Write((float)ob.Size, NpgsqlDbType.Real);
                // ReSharper restore MethodHasAsyncOverloadWithCancellation
            }

            await writer.CompleteAsync(cancellationToken);
        }
    }
}