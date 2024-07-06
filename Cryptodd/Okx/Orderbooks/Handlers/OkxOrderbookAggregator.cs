using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Cryptodd.Algorithms.Topk;
using Cryptodd.IoC;
using Cryptodd.Okx.Models;
using Cryptodd.Okx.Models.HttpResponse;
using MathNet.Numerics.Random;
using Maxisoft.Utils.Collections.Lists;
using Maxisoft.Utils.Collections.Lists.Specialized;
using Maxisoft.Utils.Disposables;
using Towel;

namespace Cryptodd.Okx.Orderbooks.Handlers;

// ReSharper disable once UnusedType.Global
public sealed class OkxOrderbookAggregator : IService, IOkxOrderbookAggregator
{
    public const int Size = 25;

    private static readonly Xoshiro256StarStar Random = new(true);

    public ValueTask<OkxAggregatedOrderbookHandlerArguments> Handle(OkxWebsocketOrderbookResponse response,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var asks = response.FirstData.asks;
        var bids = response.FirstData.bids;


        var askTriplets = Process<NegateIndex>(asks);
        var bidTriplets = Process<DirectIndex>(bids);


        return ValueTask.FromResult(new OkxAggregatedOrderbookHandlerArguments(
            askTriplets,
            bidTriplets,
            response
        ));
    }

    private static ArrayList<int> GetFlatIndices<TIndexTransformer>(PooledList<OkxOrderbookEntry> view)
        where TIndexTransformer : struct, IIndexTransformer
    {
        TIndexTransformer indexTransformer = default;
        var isDirect = indexTransformer.Transform(1) == 1;

        var counterTopK = new TopK<(int counterLog, int volumeLog, int index)>(Size);
        var volumeTopK = new TopK<(int volumeLog, int index)>(Size);
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
                var volumeLog = double.ILogB(entry.Quantity + 1);
                counterTopK.Add((int.Log2(entry.Count + 1), volumeLog, iT));
                volumeTopK.Add((volumeLog, iT));
            }

            lastIndex = i;
            Debug.Assert(lastIndex == view.Count - 1);
        }


        var indicesEnumerators = new[]
        {
            volumeTopK.Where((_, i) => (i & 1) != 0).Select(t => indexTransformer.Revert(t.index)).GetEnumerator(),
            volumeTopK.Where((_, i) => (i & 1) == 0).Select(t => indexTransformer.Revert(t.index)).GetEnumerator(),
            counterTopK.Select(t => indexTransformer.Revert(t.index)).GetEnumerator()
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
        var bitArray = new BitArray(view.Count)
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
                if (bitArray[index])
                {
                    continue;
                }

                bitArray[index] = true;
                flatIndices.Add(index);
                break;
            }

            if (!moved)
            {
                break;
            }

            lastEnumeratorIndex = checked(lastEnumeratorIndex + random + i + 1) % indicesEnumerators.Length;
        }


        if (flatIndices.Count > Size)
        {
            var originalFlatIndices = flatIndices.Data();
            var originalLength = flatIndices.Count;
            flatIndices = new ArrayList<int>(Size) { isDirect ? lastIndex : 0 };
            Random.NextUnique(Size - 1, 1, originalLength - 1,
                i => flatIndices.Add(originalFlatIndices[i]));
        }

        flatIndices.AsSpan().Sort();

        Debug.Assert(flatIndices.Count <= Size);

        return flatIndices;
    }

    private static OkxOrderbookEntry[] Process<TIndexTransformer>(PooledList<OkxOrderbookEntry> view)
        where TIndexTransformer : struct, IIndexTransformer
    {
        TIndexTransformer indexTransformer = default;
        var isDirect = indexTransformer.Transform(1) == 1;

        var flatIndices = GetFlatIndices<TIndexTransformer>(view);

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
                tuples[j] = new OkxOrderbookEntry(prev.Price, Count: 0, Quantity: 0);
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
                tuples[j] = new OkxOrderbookEntry(prev.Price, Count: 0, Quantity: 0);
            }

            tuples.AsSpan().Reverse();
            return tuples;
        }
    }

    #region IndexTransformer

    private interface IIndexTransformer
    {
        int Transform(int value);
        int Revert(int value);
    }

    private struct DirectIndex : IIndexTransformer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Transform(int value)
        {
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Revert(int value)
        {
            return value;
        }
    }

    private struct NegateIndex : IIndexTransformer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Transform(int value)
        {
            return -value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Revert(int value)
        {
            return -value;
        }
    }

    #endregion
}