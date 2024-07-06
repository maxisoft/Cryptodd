using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Cryptodd.Algorithms;
using Cryptodd.Algorithms.Topk;
using Cryptodd.IoC;
using Cryptodd.OrderBooks;
using MathNet.Numerics.Random;
using Maxisoft.Utils.Collections.Lists;
using Maxisoft.Utils.Disposables;
using Maxisoft.Utils.Empties;
using Towel;

namespace Cryptodd.Binance.Orderbooks.Handlers;

public interface IOrderbookAggregator : IBinanceOrderbookHandler<BinanceAggregatedOrderbookHandlerArguments>
{
}

// ReSharper disable once UnusedType.Global
public sealed class BinanceOrderbookAggregator : IService, IOrderbookAggregator
{
    public const int Size = 128;

    public ValueTask<BinanceAggregatedOrderbookHandlerArguments> Handle(BinanceOrderbookHandlerArguments arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var asks = arguments.Asks;
        var bids = arguments.Bids;


        var (askTriplets, askCount, askTime) = ProcessView<NegateIndex>(asks, filterOutZeroChange: true);
        if (askCount * 1f / askTriplets.Length < 0.8f)
        {
            (askTriplets, askCount, askTime) = ProcessView<NegateIndex>(asks, filterOutZeroChange: false);
        }

        var (bidTriplets, bidCount, bidTime) = ProcessView<DirectIndex>(bids, filterOutZeroChange: true);
        if (bidCount * 1f / bidTriplets.Length < 0.8f)
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

    private static ArrayList<int>
        GetFlatIndices<TIndexTransformer>(InMemoryOrderbook<OrderBookEntryWithStat>.SortedView view,
            ref DateTimeOffset maxTime, bool filterOutZeroChange = true)
        where TIndexTransformer : struct, IIndexTransformer
    {
        TIndexTransformer indexTransformer = default;
        var isDirect = indexTransformer.Transform(1) == 1;

        var changeTopK = new TopK<(sbyte counterLog, int volumeLog, int counter, int index)>(Size);
        var volumeTopK = new TopK<(int volumeLog, double volume, int index)>(Size);
        var varianceTopK = new TopK<(int varianceLog, double volume, int index)>(Size);
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
                var volumeLog = Math.ILogB(entry.Quantity + 1);
                var stats = entry.Statistics;
                var volumeMean = stats is { Count: > 1 } ? stats.Mean : entry.Quantity;
                changeTopK.Add(((sbyte)int.Log2(entry.ChangeCounter + 1), volumeLog, entry.ChangeCounter, iT));
                volumeTopK.Add((volumeLog, volumeMean, iT));
                
                if (stats is { Count: >= 8 })
                {
                    varianceTopK.Add((Math.ILogB(stats.PopulationVariance + 1), stats.Mean, iT));
                }

                if (maxTime < entry.Time)
                {
                    maxTime = entry.Time;
                }
            }

            lastIndex = i;
            Debug.Assert(lastIndex == view.Count - 1);
        }

        var indicesEnumerators = new[]
        {
            volumeTopK.Where((_, i) => (i & 1) != 0).Select(t => indexTransformer.Revert(t.index)).GetEnumerator(),
            volumeTopK.Where((_, i) => (i & 1) == 0).Select(t => indexTransformer.Revert(t.index)).GetEnumerator(),
            changeTopK.Select(t => indexTransformer.Revert(t.index)).GetEnumerator(),
            varianceTopK.Select(t => indexTransformer.Revert(t.index)).GetEnumerator(),
        };

        using var dm = new DisposableManager();
        foreach (var enumerator in indicesEnumerators)
        {
            dm.LinkDisposable(enumerator);
        }


        ArrayList<int> flatIndices = new(Size)
        {
            isDirect ? lastIndex : 0
        };
        var indiceBitArray = new BitArray(view.Count)
        {
            [flatIndices.Front()] = true
        };


        var lastEnumeratorIndex = 0;
        while (flatIndices.Count < Size)
        {
            int i;
            var moved = false;
            var random = Random.NextBool() ? Random.Next(indicesEnumerators.Length) : 0;
            for (i = 0; i < indicesEnumerators.Length; ++i)
            {
                var enumerator =
                    indicesEnumerators[checked(i + lastEnumeratorIndex + random) % indicesEnumerators.Length];
                if (!enumerator.MoveNext())
                {
                    continue;
                }

                moved = true;
                var index = enumerator.Current;
                if (indiceBitArray[index])
                {
                    continue;
                }

                indiceBitArray[index] = true;
                flatIndices.Add(index);
                break;
            }

            if (!moved)
            {
                break;
            }

            lastEnumeratorIndex = checked(lastEnumeratorIndex + random + i + 1) % indicesEnumerators.Length;
        }


        var originalFlatIndices = flatIndices.Data();

        if (flatIndices.Count > Size)
        {
            var originalLength = flatIndices.Count;
            flatIndices = new ArrayList<int>(Size) { isDirect ? lastIndex : 0 };
            Random.NextUnique(count: Size - 1, minValue: 1, maxValue: originalLength - 1,
                i => flatIndices.Add(originalFlatIndices[i]));
        }

        flatIndices.AsSpan().Sort();

        Debug.Assert(flatIndices.Count <= Size);

        return flatIndices;
    }

    private static (DetailedOrderbookEntryFloatTuple[] triplets, int count, DateTimeOffset maxTime)
        ProcessView<TIndexTransformer>(InMemoryOrderbook<OrderBookEntryWithStat>.SortedView view,
            bool filterOutZeroChange = true) where TIndexTransformer : struct, IIndexTransformer
    {
        TIndexTransformer indexTransformer = default;
        var isDirect = indexTransformer.Transform(1) == 1;
        var maxTime = DateTimeOffset.UnixEpoch;
        var flatIndices = GetFlatIndices<TIndexTransformer>(view, ref maxTime, filterOutZeroChange);
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
                var sizeStd = ExponentialMovingAverage.FromSpan(Math.Abs(nextIndex - index));
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
                    if (istats is { Count: > 4 })
                    {
                        var std = (float)istats.PopulationStandardDeviation;
                        if (sizeStd.Value <= 0)
                        {
                            sizeStd.Value = std;
                        }

                        sizeStd.Push(std);
                    }

                    aggregateCount++;
                }

                var stats = entry.Statistics;
                if (stats is { Count: >= 4 })
                {
                    var std = (float)stats.StandardDeviation;
                    if (sizeStd.Value > 0)
                    {
                        if (std > 0)
                        {
                            sizeStd.Value = (0.8f * std + 0.2f * sizeStd.Value);
                        }
                    }
                    else
                    {
                        sizeStd.Value = std;
                    }
                }

                tuples[i] = new DetailedOrderbookEntryFloatTuple(
                    Price: (float)entry.Price,
                    Size: (float)sumSize,
                    RawSize: (float)entry.Quantity,
                    MeanPrice: (float)(sumPriceVolume / sumSize),
                    ChangeCounter: entry.ChangeCounter,
                    TotalChangeCounter: totalChangeCounter,
                    SizeStd: sizeStd.Value,
                    AggregateCount: aggregateCount
                );
            }

            // fill to have a nice representation
            for (var j = Math.Max(i + 1, 1); j < tuples.Length; j++)
            {
                ref var prev = ref tuples[j - 1];
                tuples[j] = prev with
                {
                    Size = 0, RawSize = 0, Price = prev.MeanPrice, ChangeCounter = 0, TotalChangeCounter = 0,
                    SizeStd = 0, AggregateCount = 0
                };
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
                var sizeStd = ExponentialMovingAverage.FromSpan(Math.Abs(nextIndex - index));
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
                    if (istats is { Count: > 4 })
                    {
                        var std = (float)istats.PopulationStandardDeviation;
                        if (sizeStd.Value <= 0)
                        {
                            sizeStd.Value = std;
                        }

                        sizeStd.Push(std);
                    }

                    aggregateCount++;
                }

                var stats = entry.Statistics;
                if (stats is { Count: >= 4 })
                {
                    var std = (float)stats.StandardDeviation;
                    if (sizeStd.Value > 0)
                    {
                        if (std > 0)
                        {
                            sizeStd.Value = (0.8f * std + 0.2f * sizeStd.Value);
                        }
                    }
                    else
                    {
                        sizeStd.Value = std;
                    }
                }

                tuples[i] = new DetailedOrderbookEntryFloatTuple(
                    Price: (float)entry.Price,
                    Size: (float)sumSize,
                    RawSize: (float)entry.Quantity,
                    MeanPrice: (float)(sumPriceVolume / sumSize),
                    ChangeCounter: entry.ChangeCounter,
                    TotalChangeCounter: totalChangeCounter,
                    SizeStd: sizeStd.Value,
                    AggregateCount: aggregateCount
                );
            }

            // fill to have a nice representation
            for (var j = Math.Max(i + 1, 1); j < tuples.Length; j++)
            {
                ref var prev = ref tuples[j - 1];
                tuples[j] = prev with
                {
                    Size = 0, RawSize = 0, Price = prev.MeanPrice, ChangeCounter = 0, TotalChangeCounter = 0,
                    SizeStd = 0, AggregateCount = 0
                };
            }

            tuples.AsSpan().Reverse();
            return (tuples, i + 1, maxTime);
        }
    }

    private static readonly Xoshiro256StarStar Random = new Xoshiro256StarStar(threadSafe: true);

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