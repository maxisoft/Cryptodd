using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cryptodd.FileSystem;
using Cryptodd.Ftx.Models;
using Cryptodd.Ftx.Models.DatabasePoco;
using Cryptodd.Pairs;
using Microsoft.Extensions.Configuration;
using Parquet;
using Parquet.Data;
using Serilog;
using FutureStats = Cryptodd.Ftx.Models.DatabasePoco.FutureStats;

namespace Cryptodd.Ftx.Futures;

public class SaveFuturesStatsToParquetHandler : IFuturesStatsHandler
{
    public const string FileType = "parquet";
    public const string DefaultFileName = "ftx_futures_stats.parquet";
    private const int ChunkSize = 128;
    private readonly IConfiguration _configuration;
    private readonly IPairFilterLoader _pairFilterLoader;
    private readonly IPathResolver _pathResolver;
    private readonly ILogger _logger;

    public SaveFuturesStatsToParquetHandler(IConfiguration configuration, IPairFilterLoader pairFilterLoader, IPathResolver pathResolver, ILogger logger)
    {
        _configuration = configuration;
        _pairFilterLoader = pairFilterLoader;
        _pathResolver = pathResolver;
        _logger = logger;
    }
    public bool Disabled { get; set; }

    public async Task Handle(IReadOnlyCollection<FutureStats> futureStats, CancellationToken cancellationToken)
    {
        if (!futureStats.Any())
        {
            return;
        }

        Debug.Assert(!Disabled, "Disabled state must be checked externally");
        if (Disabled)
        {
            return;
        }
        
        var section = _configuration.GetSection("Ftx").GetSection("FutureStats").GetSection("Parquet");
        if (!section.GetValue("Enabled", false))
        {
            return;
        }
        
        var fileName = section.GetValue<string>("File", DefaultFileName);
        fileName = _pathResolver.Resolve(fileName,
            new ResolveOption
            {
                Namespace = GetType().Namespace!, FileType = FileType,
                IntendedAction = FileIntendedAction.Append | FileIntendedAction.Read | FileIntendedAction.Create |
                                 FileIntendedAction.Write
            });
        var pairFilter = await _pairFilterLoader.GetPairFilterAsync("Ftx.FutureStats.Parquet", cancellationToken)
            .ConfigureAwait(false);
        
        foreach (var statsArray in futureStats
                     .OrderBy(details => details.Mark)
                     .Chunk(section.GetValue("ChunkSize", ChunkSize)))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            await Task.Factory.StartNew(() => SaveToParquet(statsArray, fileName), cancellationToken)
                .ConfigureAwait(false);
        }

        _logger.Verbose("Saved {Count} Grouped Orderbooks to parquet", futureStats.Count);
    }
    
    private static void SaveToParquet(IReadOnlyCollection<FutureStats> stats, string fileName)
    {
        var timeColumn = new DataField("time", DataType.Int64, false);
        var marketHashColumn = new DataField("market_hash", DataType.Int64, false);
        var openInterestColumn = new DataField("open_interest", DataType.Double, false);
        var openInterestUsdColumn = new DataField("open_interest_usd", DataType.Double, false);
        var nextFundingRateColumn = new DataField("next_funding_rate", DataType.Float, false);
        var spreadColumn = new DataField("spread", DataType.Float, false);
        var markColumn = new DataField("mark", DataType.Float, false);

        var schema = new Schema(timeColumn, marketHashColumn, openInterestColumn, openInterestUsdColumn, nextFundingRateColumn, spreadColumn, markColumn);

        var exists = File.Exists(fileName);
        using Stream fileStream = File.Open(fileName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        using var parquetWriter = new ParquetWriter(schema, fileStream, append: exists);

        // create a new row group in the file
        using var groupWriter = parquetWriter.CreateRowGroup();
        groupWriter.WriteColumn(new DataColumn(timeColumn, stats.Select(o => o.Time).ToArray()));
        groupWriter.WriteColumn(new DataColumn(marketHashColumn, stats.Select(o => o.MarketHash).ToArray()));
        groupWriter.WriteColumn(new DataColumn(openInterestColumn, stats.Select(o => o.OpenInterest).ToArray()));
        groupWriter.WriteColumn(new DataColumn(openInterestUsdColumn, stats.Select(o => o.OpenInterestUsd).ToArray()));
        groupWriter.WriteColumn(new DataColumn(nextFundingRateColumn, stats.Select(o => o.NextFundingRate).ToArray()));
        groupWriter.WriteColumn(new DataColumn(spreadColumn, stats.Select(o => o.Spread).ToArray()));
        groupWriter.WriteColumn(new DataColumn(markColumn, stats.Select(o => o.Mark).ToArray()));
    }
}