using System.Diagnostics;
using Cryptodd.Bitfinex.Models;
using Cryptodd.FileSystem;
using Cryptodd.Pairs;
using Maxisoft.Utils.Collections.Lists;
using Microsoft.Extensions.Configuration;
using Parquet;
using Parquet.Data;
using Serilog;
using SmartFormat;

namespace Cryptodd.Bitfinex.Orderbooks;

/// <summary>
///     Save raw orderbook into a parquet database
/// </summary>
public class SaveOrderbookToParquetHandler : IOrderbookHandler
{
    public const string FileType = "parquet";
    public const string DefaultFileName = "bitfinex_orderbook";
    private const int ChunkSize = 128;
    private readonly IConfiguration _configuration;
    private readonly ILogger _logger;
    private readonly IPairFilterLoader _pairFilterLoader;
    private readonly IPathResolver _pathResolver;

    public SaveOrderbookToParquetHandler(IConfiguration configuration, IPathResolver pathResolver,
        IPairFilterLoader pairFilterLoader, ILogger logger)
    {
        _configuration = configuration;
        _pathResolver = pathResolver;
        _pairFilterLoader = pairFilterLoader;
        _logger = logger.ForContext(GetType());
    }

    public async Task Handle(OrderbookHandlerQuery query, IReadOnlyCollection<OrderbookEnvelope> orderbooks,
        CancellationToken cancellationToken)
    {
        if (!orderbooks.Any())
        {
            return;
        }

        Debug.Assert(!Disabled, "Disabled state must be checked externally");
        if (Disabled)
        {
            return;
        }

        var section = _configuration.GetSection("Bitfinex").GetSection("OrderBook").GetSection("Parquet");
        if (!section.GetValue("Enabled", false))
        {
            return;
        }

        var fileName = section.GetValue<string>("File", "");
        if (string.IsNullOrEmpty(fileName))
        {
            fileName = $"{DefaultFileName}_p{query.Precision}_l{query.Length}.{FileType}";
        }
        fileName = _pathResolver.Resolve(fileName,
            new ResolveOption
            {
                Namespace = GetType().Namespace!, FileType = FileType,
                IntendedAction = FileIntendedAction.Append | FileIntendedAction.Read | FileIntendedAction.Create |
                                 FileIntendedAction.Write
            });
        var gzip = section.GetValue("HighCompression", false); // use snappy else
        var pairFilter = await _pairFilterLoader
            .GetPairFilterAsync("Bitfinex.GroupedOrderBook.Parquet", cancellationToken)
            .ConfigureAwait(false);

        foreach (var array in orderbooks
                     .Where(details => pairFilter.Match(details.Symbol))
                     .OrderBy(details => details.Symbol)
                     .Chunk(section.GetValue("ChunkSize", ChunkSize)))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            await SaveToParquetAsync(array, fileName, gzip, cancellationToken).ConfigureAwait(false);
        }

        _logger.Debug("Saved {Count} Grouped Orderbooks to parquet", orderbooks.Count);
    }

    public bool Disabled { get; set; }

    private async Task SaveToParquetAsync(IReadOnlyCollection<OrderbookEnvelope> orderbooks, string fileName,
        bool gzip, CancellationToken cancellationToken)
    {
        if (!orderbooks.Any())
        {
            return;
        }

        var timeColumn = new DataField("time", DataType.Int64, false);
        var marketColumn = new DataField("symbol", DataType.Int64, false);

        var orderBookDeepness = orderbooks.FirstOrDefault()?.Orderbook.Count / 2 ?? 0;

        var columns = new ArrayList<DataField>(2 + orderBookDeepness * 2)
        {
            timeColumn,
            marketColumn
        };

        for (var i = 0; i < orderBookDeepness; i++)
        {
            columns.Add(new DataField($"bid_price_{i + 1}", DataType.Double, false));
            columns.Add(new DataField($"bid_count_{i + 1}", DataType.Int32, false));
            columns.Add(new DataField($"bid_size_{i + 1}", DataType.Double, false));
        }

        for (var i = 0; i < orderBookDeepness; i++)
        {
            columns.Add(new DataField($"ask_price_{i + 1}", DataType.Double, false));
            columns.Add(new DataField($"ask_count_{i + 1}", DataType.Int32, false));
            columns.Add(new DataField($"ask_size_{i + 1}", DataType.Double, false));
        }

        var schema = new Schema(columns);

        var exists = File.Exists(fileName) && new FileInfo(fileName).Length > 0;
        using Stream fileStream = File.Open(fileName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        using var parquetWriter = await ParquetWriter.CreateAsync(schema, fileStream, append: exists, cancellationToken: cancellationToken);
        if (gzip)
        {
            parquetWriter.CompressionMethod = CompressionMethod.Gzip;
        }

        // create a new row group in the file
        using var groupWriter = parquetWriter.CreateRowGroup();
        await groupWriter.WriteColumnAsync(new DataColumn(timeColumn,
            orderbooks.Select(o => o.Time).ToArray()), cancellationToken).ConfigureAwait(false);
        await groupWriter.WriteColumnAsync(new DataColumn(marketColumn,
            orderbooks.Select(o => PairHasher.Hash(o.Symbol)).ToArray()), cancellationToken).ConfigureAwait(false);

        var dataFields = columns.ToArray()[2..];

        Debug.Assert(dataFields[0].Name == "bid_price_1");
        Debug.Assert(dataFields[2].Name == "bid_size_1");

        for (var i = 0; i < orderBookDeepness; i++)
        {
            await groupWriter.WriteColumnAsync(new DataColumn(dataFields[0],
                orderbooks.Select(orderbook => orderbook.Orderbook[i].Price).ToArray()), cancellationToken);
            await groupWriter.WriteColumnAsync(new DataColumn(dataFields[1],
                orderbooks.Select(orderbook => orderbook.Orderbook[i].Count).ToArray()), cancellationToken);
            await groupWriter.WriteColumnAsync(new DataColumn(dataFields[2],
                orderbooks.Select(orderbook => orderbook.Orderbook[i].Size).ToArray()), cancellationToken);
            dataFields = dataFields[3..];
        }

        Debug.Assert(dataFields[0].Name == "ask_price_1");
        Debug.Assert(dataFields[2].Name == "ask_size_1");

        for (var i = 0; i < orderBookDeepness; i++)
        {
            await groupWriter.WriteColumnAsync(new DataColumn(dataFields[0],
                orderbooks.Select(orderbook => orderbook.Orderbook[i + orderBookDeepness].Price).ToArray()), cancellationToken);
            await groupWriter.WriteColumnAsync(new DataColumn(dataFields[1],
                orderbooks.Select(orderbook => orderbook.Orderbook[i + orderBookDeepness].Count).ToArray()), cancellationToken);
            await groupWriter.WriteColumnAsync(new DataColumn(dataFields[2],
                orderbooks.Select(orderbook => orderbook.Orderbook[i + orderBookDeepness].Size).ToArray()), cancellationToken);
            dataFields = dataFields[3..];
        }

        Debug.Assert(dataFields.Length == 0);
    }
}