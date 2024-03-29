﻿using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cryptodd.Ftx.Models;
using Cryptodd.Ftx.Models.DatabasePoco;
using Cryptodd.IO.FileSystem;
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

            await SaveToParquetAsync(statsArray, fileName, cancellationToken)
                .ConfigureAwait(false);
        }

        _logger.Verbose("Saved {Count} Grouped Orderbooks to parquet", futureStats.Count);
    }
    
    private static async Task SaveToParquetAsync(IReadOnlyCollection<FutureStats> stats, string fileName, CancellationToken cancellationToken)
    {
        var timeColumn = new DataField("time", DataType.Int64, false);
        var marketHashColumn = new DataField("market_hash", DataType.Int64, false);
        var openInterestColumn = new DataField("open_interest", DataType.Double, false);
        var openInterestUsdColumn = new DataField("open_interest_usd", DataType.Double, false);
        var nextFundingRateColumn = new DataField("next_funding_rate", DataType.Float, false);
        var spreadColumn = new DataField("spread", DataType.Float, false);
        var markColumn = new DataField("mark", DataType.Float, false);

        var schema = new Schema(timeColumn, marketHashColumn, openInterestColumn, openInterestUsdColumn, nextFundingRateColumn, spreadColumn, markColumn);

        var exists = File.Exists(fileName) && new FileInfo(fileName).Length > 0;
        await using Stream fileStream = File.Open(fileName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        using var parquetWriter = await ParquetWriter.CreateAsync(schema, fileStream, append: exists, cancellationToken: cancellationToken);

        // create a new row group in the file
        using var groupWriter = parquetWriter.CreateRowGroup();
        await groupWriter.WriteColumnAsync(new DataColumn(timeColumn, stats.Select(o => o.Time).ToArray()), cancellationToken);
        await groupWriter.WriteColumnAsync(new DataColumn(marketHashColumn, stats.Select(o => o.MarketHash).ToArray()), cancellationToken);
        await groupWriter.WriteColumnAsync(new DataColumn(openInterestColumn, stats.Select(o => o.OpenInterest).ToArray()), cancellationToken);
        await groupWriter.WriteColumnAsync(new DataColumn(openInterestUsdColumn, stats.Select(o => o.OpenInterestUsd).ToArray()), cancellationToken);
        await groupWriter.WriteColumnAsync(new DataColumn(nextFundingRateColumn, stats.Select(o => o.NextFundingRate).ToArray()), cancellationToken);
        await groupWriter.WriteColumnAsync(new DataColumn(spreadColumn, stats.Select(o => o.Spread).ToArray()), cancellationToken);
        await groupWriter.WriteColumnAsync(new DataColumn(markColumn, stats.Select(o => o.Mark).ToArray()), cancellationToken);
    }
}