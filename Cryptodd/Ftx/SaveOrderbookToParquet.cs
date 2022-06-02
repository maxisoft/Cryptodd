using System.Diagnostics;
using Cryptodd.FileSystem;
using Cryptodd.Ftx.Models;
using Microsoft.Extensions.Configuration;
using Parquet;
using Parquet.Data;

namespace Cryptodd.Ftx;

public class SaveOrderbookToParquet : IGroupedOrderbookHandler
{
    private readonly IConfiguration _configuration;
    private readonly IPathResolver _pathResolver;

    public SaveOrderbookToParquet(IConfiguration configuration, IPathResolver pathResolver)
    {
        _configuration = configuration;
        _pathResolver = pathResolver;
    }

    private static void SaveToParquet(IReadOnlyCollection<GroupedOrderbookDetails> orderbooks, string fileName, bool gzip)
    {
        var timeColumn = new DataField("time", DataType.Int64, hasNulls: false);
        var marketColumn = new DataField("market", DataType.String, hasNulls: false);
        var groupingColumn = new DataField("grouping", DataType.Double, hasNulls: false);
        var bidsColumn = new DataField("bid", DataType.Double, isArray: true);
        var asksColumn = new DataField("ask", DataType.Double, isArray: true);
        
        var schema = new Schema(timeColumn, marketColumn, groupingColumn, bidsColumn, asksColumn);
        
        var exists = File.Exists(fileName);
        using Stream fileStream = File.Open(fileName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        using var parquetWriter = new ParquetWriter(schema, fileStream, append: exists);
        if (gzip)
        {
            parquetWriter.CompressionMethod = CompressionMethod.Gzip;
        }
        // create a new row group in the file
        using var groupWriter = parquetWriter.CreateRowGroup();
        groupWriter.WriteColumn(new DataColumn(timeColumn, orderbooks.Select(o => o.Time.ToUnixTimeMilliseconds()).ToArray()));
        groupWriter.WriteColumn(new DataColumn(marketColumn, orderbooks.Select(o => o.Market).ToArray()));
        groupWriter.WriteColumn(new DataColumn(groupingColumn, orderbooks.Select(o => o.Grouping).ToArray()));
        groupWriter.WriteColumn(new DataColumn(bidsColumn, orderbooks.Select(o => o.Data.Bids.SelectMany(b => new double[] {b.Price, b.Size})).SelectMany(doubles => doubles).ToArray()));
        groupWriter.WriteColumn(new DataColumn(asksColumn, orderbooks.Select(o => o.Data.Asks.SelectMany(a => new double[] {a.Price, a.Size})).SelectMany(doubles => doubles).ToArray()));
    }
    
    public Task Handle(IReadOnlyCollection<GroupedOrderbookDetails> orderbooks, CancellationToken cancellationToken)
    {
        Debug.Assert(!Disabled);
        var section = _configuration.GetSection("Ftx.GroupedOrderBook.Parquet");
        if (!section.GetValue<bool>("Enable", true)) return Task.CompletedTask;
        var fileName = section.GetValue<string>("File", "ftx_grouped_orderbook.parquet");
        fileName = _pathResolver.Resolve(fileName);
        var gzip = section.GetValue<bool>("HighCompression", false); // use snappy else
        return Task.Factory.StartNew(() => SaveToParquet(orderbooks, fileName, gzip), cancellationToken);
    }

    public bool Disabled { get; set; }
}