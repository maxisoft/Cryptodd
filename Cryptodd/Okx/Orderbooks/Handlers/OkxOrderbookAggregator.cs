using System.Diagnostics;
using System.Runtime.CompilerServices;
using Cryptodd.Algorithms;
using Cryptodd.Algorithms.Topk;
using Cryptodd.Binance.Orderbooks;
using Cryptodd.Binance.Orderbooks.Handlers;
using Cryptodd.IoC;
using Cryptodd.Okx.Models;
using Cryptodd.Okx.Models.HttpResponse;
using Cryptodd.OrderBooks;
using MathNet.Numerics.Random;
using Maxisoft.Utils.Collections.Lists;
using Maxisoft.Utils.Collections.Lists.Specialized;
using Towel;

namespace Cryptodd.Okx.Orderbooks.Handlers;

// ReSharper disable once UnusedType.Global
public sealed class OkxOrderbookAggregator : IService, IOkxOrderbookAggregator
{
    public const int Size = 25;

    public ValueTask<OkxAggregatedOrderbookHandlerArguments> Handle(OkxWebsocketOrderbookResponse response,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var asks = response.FirstData.asks;
        var bids = response.FirstData.bids;

        static OkxOrderbookEntry[] Process<
            TIndexTransformer>(
            PooledList<OkxOrderbookEntry> view)
            where TIndexTransformer : struct, IIndexTransformer
        {
            TIndexTransformer indexTransformer = default;
            var isDirect = indexTransformer.Transform(1) == 1;

            var changeTopK = new TopK<(int counter, double volume, int index)>(Size);
            var volumeTopK = new TopK<(double volume, int index)>(Size);
            int lastIndex;
            {
                var i = -1;
                foreach (var entry in view)
                {
                    i++;
                    if (entry.Price <= 0 || entry.Quantity <= 0)
                    {
                        continue;
                    }

                    var iT = indexTransformer.Transform(i);
                    changeTopK.Add((entry.Count, entry.Quantity, iT));
                    volumeTopK.Add((entry.Quantity, iT));
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

                if (indices.Count == 0)
                {
                    throw new ArgumentException("empty orderbook");
                }

                flatIndices = new ArrayList<int>(indices.ToArray());
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
            var tuples = new OkxOrderbookEntry[Size];

            // for ask
            if (!isDirect)
            {
                var i = -1;
                var prevIndex = -1;
                foreach (var flatIndex in flatIndices.AsSpan())
                {
                    i++;
                    var index = flatIndex;
                    OkxOrderbookEntry entry;
                    do
                    {
                        if (index == prevIndex)
                        {
                            index++;
                            continue;
                        }

                        entry = view[index];
                        if (entry.Price <= 0 || entry.Quantity <= 0)
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
                    }

                    entry = view[index];

                    prevIndex = index;
                    var correctNext = i + 1 < flatIndices.Count;
                    var nextIndex = correctNext ? flatIndices[i + 1] : view.Count - 1;

                    var sumSize = entry.Quantity;
                    var sumPriceVolume = entry.Price * entry.Quantity;
                    var sumCount = entry.Count;

                    for (var j = nextIndex - (correctNext ? 1 : 0); j > index; j--)
                    {
                        var intermediateEntry = view[j];

                        var quantity = intermediateEntry.Quantity;
                        if (quantity <= 0)
                        {
                            continue;
                        }

                        sumSize += quantity;
                        sumPriceVolume += intermediateEntry.Price * quantity;
                        sumCount += intermediateEntry.Count;
                    }

                    tuples[i] = new OkxOrderbookEntry((float)(sumPriceVolume / sumSize), (float)sumSize, sumCount);
                }

                // fill to have a nice representation
                for (var j = Math.Max(i + 1, 1); j < tuples.Length; j++)
                {
                    ref var prev = ref tuples[j - 1];
                    tuples[j] = new OkxOrderbookEntry(Price: prev.Price, Count: 0, Quantity: 0);
                }

                return tuples;
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
                    OkxOrderbookEntry entry;
                    do
                    {
                        if (index == prevIndex)
                        {
                            index--;
                            continue;
                        }

                        entry = view[index];
                        if (entry.Price <= 0 || entry.Quantity <= 0)
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
                    }

                    entry = view[index];

                    prevIndex = index;
                    var correctNext = rindex > 0;
                    var nextIndex = correctNext ? flatIndices[rindex - 1] : 0;

                    var sumSize = entry.Quantity;
                    var sumPriceVolume = entry.Price * entry.Quantity;
                    var sumCount = entry.Count;

                    for (var j = index - 1; j > (correctNext ? nextIndex : -1); j--)
                    {
                        var intermediateEntry = view[j];

                        var quantity = intermediateEntry.Quantity;
                        if (quantity <= 0)
                        {
                            continue;
                        }

                        sumSize += quantity;
                        sumPriceVolume += intermediateEntry.Price * quantity;
                        sumCount += intermediateEntry.Count;
                    }

                    tuples[i] = new OkxOrderbookEntry((float)(sumPriceVolume / sumSize), (float)sumSize, sumCount);
                }

                // fill to have a nice representation
                for (var j = Math.Max(i + 1, 1); j < tuples.Length; j++)
                {
                    ref var prev = ref tuples[j - 1];
                    tuples[j] = new OkxOrderbookEntry(Price: prev.Price, Count: 0, Quantity: 0);
                }

                tuples.AsSpan().Reverse();
                return tuples;
            }
        }


        var askTriplets = Process<NegateIndex>(asks);
        var bidTriplets = Process<DirectIndex>(bids);


        return ValueTask.FromResult(new OkxAggregatedOrderbookHandlerArguments(
            Asks: askTriplets,
            Bids: bidTriplets,
            response
        ));
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