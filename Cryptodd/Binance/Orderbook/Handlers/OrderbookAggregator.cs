using System.Diagnostics;
using System.Runtime.CompilerServices;
using Cryptodd.Algorithms.Topk;
using Cryptodd.Binance.Orderbook.Handlers;
using Cryptodd.IoC;
using Cryptodd.OrderBooks;
using MathNet.Numerics.Random;
using Maxisoft.Utils.Collections.Lists;
using Towel;

namespace Cryptodd.Binance.Orderbook;

public interface IOrderbookAggregator : IBinanceOrderbookHandler<BinanceAggregatedOrderbookHandlerArguments> { }

public class OrderbookAggregator : IService, IOrderbookAggregator
{
    internal const int Size = 128;
    public ValueTask<BinanceAggregatedOrderbookHandlerArguments> Handle(BinanceOrderbookHandlerArguments arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var asks = arguments.Asks;
        var bids = arguments.Bids;

        static (DetailedOrderbookEntryFloatTuple[] triplets, int count, DateTimeOffset maxTime) ProcessView<
            TIndexTransformer>(
            InMemoryOrderbook<OrderBookEntryWithStat>.SortedView view, bool filterOutZeroChange = true)
            where TIndexTransformer : struct, IIndexTransformer
        {
            TIndexTransformer indexTransformer = default;
            var isDirect = indexTransformer.Transform(1) == 1;

            var changeTopK = new TopK<(int counter, double volume, int index)>(Size);
            var volumeTopK = new TopK<(double volume, int index)>(Size);
            var varianceTopK = new TopK<(double variance, double volume, int index)>(Size);

            var maxTime = DateTimeOffset.UnixEpoch;
            int lastIndex;
            {
                var i = -1;
                foreach (var entry in view)
                {
                    i++;
                    if ((filterOutZeroChange && entry.ChangeCounter <= 0) || entry.Price <= 0 || entry.Quantity <= 0)
                    {
                        continue;
                    }

                    var iT = indexTransformer.Transform(i);
                    changeTopK.Add((entry.ChangeCounter, entry.Quantity, iT));
                    volumeTopK.Add((entry.Quantity, iT));
                    var stats = entry.Statistics;
                    if (stats is { Count: >= 3 })
                    {
                        varianceTopK.Add((stats.PopulationVariance, stats.Mean, iT));
                    }

                    if (maxTime < entry.Time)
                    {
                        maxTime = entry.Time;
                    }
                }

                lastIndex = i;
                Debug.Assert(lastIndex == view.Count - 1);
            }


            ArrayList<int> flatIndices;
            {
                var indices = new HashSet<int>(Size);

                foreach (var tuple in changeTopK)
                {
                    indices.Add(indexTransformer.Revert(tuple.index));
                }

                foreach (var tuple in volumeTopK)
                {
                    indices.Add(indexTransformer.Revert(tuple.index));
                }

                foreach (var tuple in varianceTopK)
                {
                    indices.Add(indexTransformer.Revert(tuple.index));
                }

                if (indices.Count == 0)
                {
                    // TODO handle
                }

                flatIndices = new ArrayList<int>(indices.ToArray());
            }

            var originalFlatIndices = flatIndices.Data();
            flatIndices.AsSpan().Sort();

            if (flatIndices.Count > Size)
            {
                var originalLength = flatIndices.Count;
                flatIndices = new ArrayList<int>(Size) { isDirect ? lastIndex : 0 };
                Random.NextUnique(count: Size - 1, minValue: 1, maxValue: originalLength - 1,
                    i => flatIndices.Add(originalFlatIndices[i]));
                flatIndices.AsSpan().Sort();
            }

            Debug.Assert(flatIndices.Count <= Size);
            var obDict = view.Collection;
            var tuples = new DetailedOrderbookEntryFloatTuple[Size];

            // for ask
            if (!isDirect)
            {
                var i = -1;
                var prevIndex = -1;
                foreach (var flatIndex in flatIndices.AsSpan())
                {
                    i++;
                    var index = flatIndex;
                    OrderBookEntryWithStat? entry = null;
                    do
                    {
                        if (index == prevIndex)
                        {
                            index++;
                            continue;
                        }

                        var priceKey = view[index];
                        if (!obDict.TryGetValue(priceKey, out entry) || entry.Price <= 0 || entry.Quantity <= 0)
                        {
                            index++;
                        }
                        else
                        {
                            break;
                        }
                    } while (index < view.Count);

                    if (index >= view.Count)
                    {
                        index = view.Count - 1;
                        var priceKey = view[index];
                        obDict.TryGetValue(priceKey, out entry);
                    }

                    if (entry is null)
                    {
                        // TODO raise or fill triplets[i]
                        continue;
                    }

                    prevIndex = index;
                    var correctNext = i + 1 < flatIndices.Count;
                    var nextIndex = correctNext ? flatIndices[i + 1] : view.Count - 1;

                    var sumSize = entry.Quantity;
                    var sumPriceVolume = entry.Price * entry.Quantity;
                    var aggregateCount = 1;
                    var sizeStd = 0d;
                    var totalChangeCounter = entry.ChangeCounter;

                    for (var j = nextIndex - (correctNext ? 1 : 0); j > index; j--)
                    {
                        var priceKey = view[j];
                        if (!obDict.TryGetValue(priceKey, out var intermediateEntry))
                        {
                            continue;
                        }

                        var quantity = intermediateEntry.Quantity;
                        if (quantity <= 0)
                        {
                            continue;
                        }

                        sumSize += quantity;
                        sumPriceVolume += intermediateEntry.Price * quantity;
                        totalChangeCounter += entry.ChangeCounter;
                        var istats = intermediateEntry.Statistics;
                        if (istats is { Count: > 2 })
                        {
                            sizeStd = sizeStd <= 0
                                ? istats.PopulationStandardDeviation
                                : 0.9 * sizeStd + 0.1 * istats.PopulationStandardDeviation;
                        }

                        aggregateCount++;
                    }

                    var stats = entry.Statistics;
                    if (stats is { Count: >= 2 })
                    {
                        var std = stats.StandardDeviation;
                        if (sizeStd > 0)
                        {
                            if (std > 0)
                            {
                                sizeStd = (0.8 * std + 0.2 * sizeStd);
                            }
                        }
                        else
                        {
                            sizeStd = std;
                        }
                    }

                    tuples[i] = new DetailedOrderbookEntryFloatTuple(
                        Price: (float)entry.Price,
                        Size: (float)sumSize,
                        RawSize: (float)entry.Quantity,
                        MeanPrice: (float)(sumPriceVolume / sumSize),
                        ChangeCounter: entry.ChangeCounter,
                        TotalChangeCounter: totalChangeCounter,
                        SizeStd: (float)sizeStd,
                        AggregateCount: aggregateCount
                    );
                }

                // fill to have a nice representation
                for (var j = Math.Max(i + 1, 1); j < tuples.Length; j++)
                {
                    tuples[j] = tuples[j - 1] with { Size = 0, RawSize = 0 };
                }

                return (tuples, i + 1, maxTime);
            }
            else
            {
                //bids
                var i = -1;
                var prevIndex = -1;
                var span = flatIndices.AsSpan();
                for (var rindex = span.Length - 1; rindex >= 0; rindex--)
                {
                    var flatIndex = span[rindex];
                    i++;
                    var index = flatIndex;
                    OrderBookEntryWithStat? entry = null;
                    do
                    {
                        if (index == prevIndex)
                        {
                            index--;
                            continue;
                        }

                        var priceKey = view[index];
                        if (!obDict.TryGetValue(priceKey, out entry) || entry.Price <= 0 || entry.Quantity <= 0)
                        {
                            index--;
                        }
                        else
                        {
                            break;
                        }
                    } while (index >= 0);

                    if (index < 0)
                    {
                        index = 0;
                        var priceKey = view[index];
                        obDict.TryGetValue(priceKey, out entry);
                    }

                    if (entry is null)
                    {
                        // TODO raise or fill triplets[i]
                        continue;
                    }

                    prevIndex = index;
                    var correctNext = rindex > 0;
                    var nextIndex = correctNext ? flatIndices[rindex - 1] : 0;

                    var sumSize = entry.Quantity;
                    var sumPriceVolume = entry.Price * entry.Quantity;
                    var aggregateCount = 1;
                    var sizeStd = 0d;
                    var totalChangeCounter = entry.ChangeCounter;

                    for (var j = index - 1; j > (correctNext ? nextIndex : -1); j--)
                    {
                        var priceKey = view[j];
                        if (!obDict.TryGetValue(priceKey, out var intermediateEntry))
                        {
                            continue;
                        }

                        var quantity = intermediateEntry.Quantity;
                        if (quantity <= 0)
                        {
                            continue;
                        }

                        sumSize += quantity;
                        sumPriceVolume += intermediateEntry.Price * quantity;
                        totalChangeCounter += entry.ChangeCounter;
                        var istats = intermediateEntry.Statistics;
                        if (istats is { Count: > 2 })
                        {
                            sizeStd = sizeStd <= 0
                                ? istats.PopulationStandardDeviation
                                : 0.9 * sizeStd + 0.1 * istats.PopulationStandardDeviation;
                        }

                        aggregateCount++;
                    }

                    var stats = entry.Statistics;
                    if (stats is { Count: >= 2 })
                    {
                        var std = stats.StandardDeviation;
                        if (sizeStd > 0)
                        {
                            if (std > 0)
                            {
                                sizeStd = (0.8 * std + 0.2 * sizeStd);
                            }
                        }
                        else
                        {
                            sizeStd = std;
                        }
                    }

                    tuples[i] = new DetailedOrderbookEntryFloatTuple(
                        Price: (float)entry.Price,
                        Size: (float)sumSize,
                        RawSize: (float)entry.Quantity,
                        MeanPrice: (float)(sumPriceVolume / sumSize),
                        ChangeCounter: entry.ChangeCounter,
                        TotalChangeCounter: totalChangeCounter,
                        SizeStd: (float)sizeStd,
                        AggregateCount: aggregateCount
                    );
                }

                // fill to have a nice representation
                for (var j = Math.Max(i + 1, 1); j < tuples.Length; j++)
                {
                    tuples[j] = tuples[j - 1] with { Size = 0, RawSize = 0 };
                }

                tuples.AsSpan().Reverse();
                return (tuples, i + 1, maxTime);
            }
        }


        var (askTriplets, askCount, askTime) = ProcessView<NegateIndex>(asks, filterOutZeroChange: true);
        if (askCount * 1f / askTriplets.Length < 0.8)
        {
            (askTriplets, askCount, askTime) = ProcessView<NegateIndex>(asks, filterOutZeroChange: false);
        }

        var (bidTriplets, bidCount, bidTime) = ProcessView<DirectIndex>(bids, filterOutZeroChange: true);
        if (bidCount * 1f / bidTriplets.Length < 0.8)
        {
            (bidTriplets, bidCount, bidTime) = ProcessView<DirectIndex>(bids, filterOutZeroChange: false);
        }

        var maxTime = askTime < bidTime ? bidTime : askTime;

        return ValueTask.FromResult(new BinanceAggregatedOrderbookHandlerArguments(
            Symbol: arguments.Symbol,
            DateTime: maxTime,
            Asks: askTriplets,
            Bids: bidTriplets,
            RawAsks: arguments.Asks,
            RawBids: arguments.Bids
        ));
    }

    private static readonly Xorshift Random = new Xorshift(threadSafe: true);

    #region IndexTransformer

    private interface IIndexTransformer
    {
        int Transform(int value);
        int Revert(int value);
    }

    private struct DirectIndex : IIndexTransformer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Transform(int value) => value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Revert(int value) => value;
    }

    private struct NegateIndex : IIndexTransformer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Transform(int value) => -value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Revert(int value) => -value;
    }

    #endregion
}