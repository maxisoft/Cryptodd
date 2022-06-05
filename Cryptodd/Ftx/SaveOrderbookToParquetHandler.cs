using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cryptodd.FileSystem;
using Cryptodd.Ftx.Models;
using Cryptodd.Pairs;
using Microsoft.Extensions.Configuration;
using Parquet;
using Parquet.Data;

namespace Cryptodd.Ftx;

/// <summary>
///     Save raw orderbook into a parquet database
/// </summary>
public class SaveOrderbookToParquetHandler : IGroupedOrderbookHandler
{
    private readonly IConfiguration _configuration;
    private readonly IPathResolver _pathResolver;
    private readonly IPairFilterLoader _pairFilterLoader;
    private readonly int ChunkSize = 64;

    public SaveOrderbookToParquetHandler(IConfiguration configuration, IPathResolver pathResolver,
        IPairFilterLoader pairFilterLoader)
    {
        _configuration = configuration;
        _pathResolver = pathResolver;
        _pairFilterLoader = pairFilterLoader;
    }

    public async Task Handle(IReadOnlyCollection<GroupedOrderbookDetails> orderbooks,
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

        var section = _configuration.GetSection("Ftx").GetSection("GroupedOrderBook").GetSection("Parquet");
        if (!section.GetValue("Enable", false))
        {
            return;
        }

        var fileName = section.GetValue<string>("File", "ftx_grouped_orderbook.parquet");
        fileName = _pathResolver.Resolve(fileName,
            new ResolveOption
            {
                Namespace = GetType().Namespace!, FileType = "parquet",
                IntendedAction = FileIntendedAction.Append | FileIntendedAction.Read | FileIntendedAction.Create |
                                 FileIntendedAction.Write
            });
        var gzip = section.GetValue("HighCompression", false); // use snappy else
        var pairFilter = await _pairFilterLoader.GetPairFilterAsync("Ftx.GroupedOrderBook.Parquet", cancellationToken)
            .ConfigureAwait(false);

        foreach (var array in orderbooks
                     .Where(details => pairFilter.Match(details.Market))
                     .Chunk(section.GetValue("ChunkSize", ChunkSize)))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            await Task.Factory.StartNew(() => SaveToParquet(array, fileName, gzip), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public bool Disabled { get; set; }

    private static void SaveToParquet(IReadOnlyCollection<GroupedOrderbookDetails> orderbooks, string fileName,
        bool gzip)
    {
        var timeColumn = new DataField("time", DataType.Int64, false);
        var marketColumn = new DataField("market", DataType.String, false);
        var groupingColumn = new DataField("grouping", DataType.Double, false);
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
        groupWriter.WriteColumn(new DataColumn(timeColumn,
            orderbooks.Select(o => o.Time.ToUnixTimeMilliseconds()).ToArray()));
        groupWriter.WriteColumn(new DataColumn(marketColumn, orderbooks.Select(o => o.Market).ToArray()));
        groupWriter.WriteColumn(new DataColumn(groupingColumn, orderbooks.Select(o => o.Grouping).ToArray()));
        {
            var (bids, bidsRepetitionLevels) = PackPairs<PairBidSelector>(orderbooks);
            groupWriter.WriteColumn(new DataColumn(bidsColumn,
                bids,
                bidsRepetitionLevels));
        }

        {
            var (asks, asksRepetitionLevels) = PackPairs<PairAskSelector>(orderbooks);
            groupWriter.WriteColumn(new DataColumn(asksColumn,
                asks,
                asksRepetitionLevels));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static (double?[] payload, int[] repetitions) PackPairs<TSelector>(
        IReadOnlyCollection<GroupedOrderbookDetails> orderbooks) where TSelector : struct, IPairAskBidSelector
    {
        var size = orderbooks.Sum(details => Math.Max(new TSelector().Select(details.Data).Length * 2, 1));

        var payload = new double?[size];
        var repetitions = new int[size];
        Span<double?> payloadSpan = payload;
        payloadSpan = payloadSpan[..size];
        Span<int> repetitionsSpan = repetitions;
        repetitionsSpan = repetitionsSpan[..size];
        repetitionsSpan.Fill(1);
        foreach (var details in orderbooks)
        {
            var span = MemoryMarshal.Cast<PriceSizePair, double>(new TSelector().Select(details.Data));
            for (var i = 0; i < span.Length; i++)
            {
                payloadSpan[i] = span[i];
            }

            if (span.IsEmpty)
            {
                payloadSpan[0] = null;
            }

            payloadSpan = payloadSpan[Math.Max(span.Length, 1)..];
            repetitionsSpan[0] = 0;
            repetitionsSpan = repetitionsSpan[Math.Max(span.Length, 1)..];
        }

        Debug.Assert(repetitionsSpan.IsEmpty);
        Debug.Assert(payloadSpan.IsEmpty);

        return (payload, repetitions);
    }

    private interface IPairAskBidSelector
    {
        Span<PriceSizePair> Select(GroupedOrderbook orderbook);
    }

    private struct PairAskSelector : IPairAskBidSelector
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public Span<PriceSizePair> Select(GroupedOrderbook orderbook) => orderbook.Asks.AsSpan();
    }

    private struct PairBidSelector : IPairAskBidSelector
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public Span<PriceSizePair> Select(GroupedOrderbook orderbook) => orderbook.Bids.AsSpan();
    }
}