using System.Diagnostics;
using Cryptodd.FileSystem;
using Cryptodd.Pairs;
using Maxisoft.Utils.Collections.Lists;
using Microsoft.Extensions.Configuration;
using Parquet;
using Parquet.Data;
using Serilog;

namespace Cryptodd.Ftx.Orderbooks.RegroupedOrderbooks;

/// <summary>
///     Save raw orderbook into a parquet database
/// </summary>
public class SaveRegroupedOrderbookToParquetHandler : IRegroupedOrderbookHandler
{
    public const string DefaultFileName = "ftx_regrouped_orderbook.parquet";
    public const string FileType = "parquet";
    private const int ChunkSize = 128;
    private readonly IConfiguration _configuration;
    private readonly ILogger _logger;
    private readonly IPairFilterLoader _pairFilterLoader;
    private readonly IPathResolver _pathResolver;

    public SaveRegroupedOrderbookToParquetHandler(IConfiguration configuration, IPathResolver pathResolver,
        IPairFilterLoader pairFilterLoader, ILogger logger)
    {
        _configuration = configuration;
        _pathResolver = pathResolver;
        _pairFilterLoader = pairFilterLoader;
        _logger = logger.ForContext(GetType());
    }

    public async Task Handle(IReadOnlyCollection<RegroupedOrderbook> orderbooks, CancellationToken cancellationToken)
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

        var section = _configuration.GetSection("Ftx").GetSection("RegroupedOrderBook").GetSection("Parquet");
        if (!section.GetValue("Enabled", true))
        {
            return;
        }

        var fileName = section.GetValue<string>("File", DefaultFileName);


        fileName = _pathResolver.Resolve(fileName, new ResolveOption
        {
            Namespace = GetType().Namespace!, FileType = FileType,
            IntendedAction = FileIntendedAction.Append | FileIntendedAction.Read | FileIntendedAction.Create |
                             FileIntendedAction.Write
        });
        var gzip = section.GetValue("HighCompression", false); // use snappy else
        var pairFilter = await _pairFilterLoader.GetPairFilterAsync("Ftx.RegroupedOrderBook.Parquet", cancellationToken)
            .ConfigureAwait(false);
        foreach (var array in orderbooks
                     .Where(ob => pairFilter.Match(ob.Market))
                     .OrderBy(ob => ob.Market)
                     .Chunk(section.GetValue("ChunkSize", ChunkSize)))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            await Task.Factory.StartNew(() => SaveToParquet(array, fileName, gzip), cancellationToken)
                .ConfigureAwait(false);
        }

        _logger.Debug("Saved {Count} Regrouped Orderbooks to parquet", orderbooks.Count);
    }

    public bool Disabled { get; set; }


    private static void SaveToParquet(IReadOnlyCollection<RegroupedOrderbook> orderbooks, string fileName,
        bool gzip)
    {
        if (!orderbooks.Any())
        {
            return;
        }

        var timeColumn = new DataField("time", DataType.Int64, false);
        var marketColumn = new DataField("market", DataType.Int64, false);

        var orderBookDeepness = orderbooks.FirstOrDefault().Asks.Length;
        if (orderbooks.FirstOrDefault().Bids.Length != orderBookDeepness)
        {
            throw new ArgumentException("Bids.Length != Asks.Length", nameof(orderbooks));
        }

        var columns = new ArrayList<DataField>(2 + orderBookDeepness * 2)
        {
            timeColumn,
            marketColumn
        };

        for (var i = 0; i < orderBookDeepness; i++)
        {
            columns.Add(new DataField($"bid_price_{i + 1}", DataType.Double, false));
            columns.Add(new DataField($"bid_size_{i + 1}", DataType.Double, false));
        }

        for (var i = 0; i < orderBookDeepness; i++)
        {
            columns.Add(new DataField($"ask_price_{i + 1}", DataType.Double, false));
            columns.Add(new DataField($"ask_size_{i + 1}", DataType.Double, false));
        }

        var schema = new Schema(columns);

        var exists = File.Exists(fileName);
        using Stream fileStream = File.Open(fileName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        using var parquetWriter = new ParquetWriter(schema, fileStream, append: exists);
        if (gzip)
        {
            parquetWriter.CompressionMethod = CompressionMethod.Gzip;
        }

        // create a new row group in the file
        using var groupWriter = parquetWriter.CreateRowGroup();
        groupWriter.WriteColumn(new DataColumn(timeColumn,
            orderbooks.Select(o => o.Time.ToUnixTimeMilliseconds()).ToArray()));
        groupWriter.WriteColumn(new DataColumn(marketColumn,
            orderbooks.Select(o => PairHasher.Hash(o.Market)).ToArray()));

        var dataFields = columns.AsSpan()[2..];

        Debug.Assert(dataFields[0].Name == "bid_price_1");
        Debug.Assert(dataFields[1].Name == "bid_size_1");

        for (var i = 0; i < orderBookDeepness; i++)
        {
            groupWriter.WriteColumn(new DataColumn(dataFields[0],
                orderbooks.Select(orderbook => orderbook.Bids[i].Price).ToArray()));
            groupWriter.WriteColumn(new DataColumn(dataFields[1],
                orderbooks.Select(orderbook => orderbook.Bids[i].Size).ToArray()));
            dataFields = dataFields[2..];
        }

        Debug.Assert(dataFields[0].Name == "ask_price_1");
        Debug.Assert(dataFields[1].Name == "ask_size_1");

        for (var i = 0; i < orderBookDeepness; i++)
        {
            groupWriter.WriteColumn(new DataColumn(dataFields[0],
                orderbooks.Select(orderbook => orderbook.Asks[i].Price).ToArray()));
            groupWriter.WriteColumn(new DataColumn(dataFields[1],
                orderbooks.Select(orderbook => orderbook.Asks[i].Size).ToArray()));
            dataFields = dataFields[2..];
        }

        Debug.Assert(dataFields.IsEmpty);
    }
}