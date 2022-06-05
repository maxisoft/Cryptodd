using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Cryptodd.Ftx;
using Cryptodd.Ftx.Models;
using Cryptodd.Ftx.Orderbooks;
using Cryptodd.Ftx.Orderbooks.RegroupedOrderbooks;
using Xunit;
using static Cryptodd.Ftx.Orderbooks.RegroupedOrderbooks.RegroupedOrderbookAlgorithm;

namespace Cryptodd.Tests.Ftx.RegroupedOrderbook;

public class TestCreateGroupedOrderbook
{
    [Fact]
    public void TestThatGroupedOrderBookIs2Double()
    {
        Assert.Equal(2 * sizeof(double), Marshal.SizeOf<PriceSizePair>());
    }
    
    [Fact]
    public async Task TestCompressBids()
    {
        var content = await GetFileContents("grouped_orderbook_btcusd.json");
        var orderbookGroupedWrapper = JsonSerializer.Deserialize<GroupedOrderbookDetails>(content,
            FtxGroupedOrderBookWebsocket.OrderBookJsonSerializerOptions);
        Assert.NotNull(orderbookGroupedWrapper);
        var bids = orderbookGroupedWrapper!.Data.Bids;
        bids.Sort();
        var result = RegroupedOrderbookAlgorithm.CompressBids(bids);
        Assert.True(result.prices.Memory.Span[..DefaultSize].SequenceEqual(new double[]
        {
            19800.0, 24580.0, 27980.0,
            30500.0, 32400.0, 33840.0, 34920.0, 35740.0, 36360.0, 36820.0, 37160.0, 37420.0, 37620.0, 37760.0, 37880.0,
            37960.0, 38020.0, 38060.0, 38080.0, 38100.0, 38120.0, 38140.0, 38160.0, 38180.0, 38200.0
        }));
        Assert.True(result.sizes.Memory.Span[..DefaultSize].SequenceEqual(new double[]
        {
            108.01340000000013, 234.44720000000015, 268.15450000000004, 417.99890000000005, 137.11249999999995,
            308.7980999999999, 244.0718999999999, 284.7162, 50.213699999999996, 55.5712, 26.9655, 98.6403, 13.4427,
            254.916, 125.107, 142.9597, 157.6387, 69.7097, 1.1207, 20.3529, 11.8699, 2.7717, 3.2607, 14.982, 0.0
        }));

        result.prices.Dispose();
        result.sizes.Dispose();
    }
    
    [Fact]
    public async Task TestCompressBidsLessThan25()
    {
        var content = await GetFileContents("grouped_orderbook_less_than_25.json");
        var orderbookGroupedWrapper = JsonSerializer.Deserialize<GroupedOrderbookDetails>(content,
            FtxGroupedOrderBookWebsocket.OrderBookJsonSerializerOptions);
        Assert.NotNull(orderbookGroupedWrapper);
        var bids = orderbookGroupedWrapper!.Data.Bids;
        bids.Sort();
        var result = RegroupedOrderbookAlgorithm.CompressBids(bids);
        Assert.True(result.prices.Memory.Span[..DefaultSize][^orderbookGroupedWrapper.Data.Bids.Count..].SequenceEqual(orderbookGroupedWrapper.Data.Bids.Select(pair => pair.Price).ToArray()));
        Assert.True(result.sizes.Memory.Span[..DefaultSize][^orderbookGroupedWrapper.Data.Bids.Count..].SequenceEqual(orderbookGroupedWrapper.Data.Bids.Select(pair => pair.Size).ToArray()));

        result.prices.Dispose();
        result.sizes.Dispose();
    }

    [Fact]
    public async Task TestDefaultPriceSizePairSortOrder()
    {
        var content = await GetFileContents("grouped_orderbook_btcusd.json");
        var orderbookGroupedWrapper = JsonSerializer.Deserialize<GroupedOrderbookDetails>(content,
            FtxGroupedOrderBookWebsocket.OrderBookJsonSerializerOptions);
        Assert.NotNull(orderbookGroupedWrapper);
        var bids = orderbookGroupedWrapper!.Data.Bids.ToArray();
        var copy = bids.ToImmutableSortedSet();
        Array.Sort(bids, (pair, otherPair) => pair.Price.CompareTo(otherPair.Price));
        Assert.Equal(bids, copy);
    }
    
    [Fact]
    public async Task TestCompressAsks()
    {
        var content = await GetFileContents("grouped_orderbook_btcusd.json");
        var orderbookGroupedWrapper = JsonSerializer.Deserialize<GroupedOrderbookDetails>(content,
            FtxGroupedOrderBookWebsocket.OrderBookJsonSerializerOptions);
        Assert.NotNull(orderbookGroupedWrapper);
        var asks = orderbookGroupedWrapper!.Data.Asks.ToArray();
        Array.Sort(asks);
        var result = RegroupedOrderbookAlgorithm.CompressAsks(asks);
        Assert.True(result.prices.Memory.Length >= DefaultSize);
        Assert.True(result.prices.Memory.Span[..DefaultSize].SequenceEqual(new double[]
        {
            38200.0, 38220.0, 38240.0, 38260.0, 38280.0, 38300.0, 38320.0, 38360.0, 38440.0, 38520.0, 38660.0, 38840.0, 39080.0, 39420.0, 39880.0, 40520.0, 41380.0, 42580.0, 44240.0, 46500.0, 49600.0, 53860.0, 59740.0, 68020.0, 84960.0
        }));
        Assert.True(result.sizes.Memory.Span[..DefaultSize].SequenceEqual(new double[]
        {
            2.394, 8.5564, 4.187, 3.9629, 1.5952, 11.8704, 18.502, 68.9306, 116.4121, 162.44979999999998, 235.12280000000004, 114.53699999999999, 48.6105, 73.4946, 35.280100000000004, 31.73090000000001, 102.24289999999999, 124.84779999999992, 225.57550000000003, 355.9464999999999, 75.45560000000002, 71.14160000000004, 12.568399999999995, 15.50890000000004, 12.683999999999926
        }));

        result.prices.Dispose();
        result.sizes.Dispose();
    }
    
    [Fact]
    public async Task TestCompressAsksLessThan25()
    {
        var content = await GetFileContents("grouped_orderbook_less_than_25.json");
        var orderbookGroupedWrapper = JsonSerializer.Deserialize<GroupedOrderbookDetails>(content,
            FtxGroupedOrderBookWebsocket.OrderBookJsonSerializerOptions);
        Assert.NotNull(orderbookGroupedWrapper);
        var asks = orderbookGroupedWrapper!.Data.Asks;
        asks.Sort();
        var result = RegroupedOrderbookAlgorithm.CompressAsks(asks);
        Assert.True(result.prices.Memory.Length >= DefaultSize);
        Assert.True(result.prices.Memory.Span[..DefaultSize][..orderbookGroupedWrapper.Data.Asks.Count].SequenceEqual(orderbookGroupedWrapper.Data.Asks.Select(pair => pair.Price).ToArray()));
        Assert.True(result.sizes.Memory.Span[..DefaultSize][..orderbookGroupedWrapper.Data.Asks.Count].SequenceEqual(orderbookGroupedWrapper.Data.Asks.Select(pair => pair.Size).ToArray()));
        
        result.prices.Dispose();
        result.sizes.Dispose();
    }

    [Fact]
    public async Task TestCreate()
    {
        var content = await GetFileContents("grouped_orderbook_btcusd.json");
        var orderbookGroupedWrapper = JsonSerializer.Deserialize<GroupedOrderbookDetails>(content,
            FtxGroupedOrderBookWebsocket.OrderBookJsonSerializerOptions);
        Assert.NotNull(orderbookGroupedWrapper);
        var regroupedOrderbook = RegroupedOrderbookAlgorithm.Create(orderbookGroupedWrapper);
        Assert.Equal(DefaultSize, regroupedOrderbook.Bids.Length);
        Assert.Equal(DefaultSize, regroupedOrderbook.Asks.Length);
    }

    private static Task<string> GetFileContents(string sampleFile)
    {
        var asm = Assembly.GetExecutingAssembly();
        var resource = $"{asm.GetName().Name}.Ftx.Resources.{sampleFile}";
        using (var stream = asm.GetManifestResourceStream(resource))
        {
            if (stream is not null)
            {
                var reader = new StreamReader(stream);
                return reader.ReadToEndAsync();
            }
        }

        return Task.FromResult(string.Empty);
    }
}