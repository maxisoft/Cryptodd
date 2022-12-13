using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Cryptodd.Binance.Models;
using Cryptodd.Utils.FastMapFork;
using Maxisoft.Utils.Collections.Lists;
using Maxisoft.Utils.Collections.Lists.Specialized;

namespace Cryptodd.Binance.Orderbook;

public partial class InMemoryOrderbook<T> where T : IOrderBookEntry, new()
{
    private readonly ConcurrentDictionary<PriceRoundKey, T> _asks = new();
    private long _asksVersion;
    private readonly ConcurrentDictionary<PriceRoundKey, T> _bids = new();
    private long _bidsVersion;

    private long _lastUpdateId = long.MinValue;

    public long LastUpdateId
    {
        get => _lastUpdateId;
        protected internal set => _lastUpdateId = Math.Max(_lastUpdateId, value);
    }

    public InMemoryOrderbook() { }

    public SortedView Asks => new AskSortedView(this);
    public SortedView Bids => new BidSortedView(this);

    public void ResetStatistics()
    {
        foreach (var (_, value) in _asks)
        {
            value.ResetStatistics();
        }

        foreach (var (_, value) in _bids)
        {
            value.ResetStatistics();
        }
    }

    private static void UpdatePart(
        ConcurrentDictionary<PriceRoundKey, T> dictionary,
        PooledList<BinancePriceQuantityEntry<double>> update,
        DateTimeOffset time,
        long updateId,
        ref long version)
    {
        foreach (var entry in update)
        {
            var key = PriceRoundKey.CreateFromPrice(entry.Price);
            if (dictionary.TryGetValue(key, out var prev))
            {
                // ReSharper disable once InvertIf
                if (updateId >= prev.UpdateId)
                {
                    if (entry.Quantity <= 0 && prev.ChangeCounter == 0 && updateId != prev.UpdateId)
                    {
                        dictionary.TryRemove(key, out _);
                        // ReSharper disable once RedundantOverflowCheckingContext
                        unchecked
                        {
                            Interlocked.Increment(ref version);
                        }
                    }
                    else
                    {
                        prev.Update(entry.Quantity, time, updateId);
                    }
                }
            }
            else
            {
                var t = new T() { Price = entry.Price, Quantity = entry.Quantity, Time = time, UpdateId = updateId };
                // ReSharper disable once InvertIf
                if (dictionary.TryAdd(key, t))
                {
                    // ReSharper disable once RedundantOverflowCheckingContext
                    unchecked
                    {
                        Interlocked.Increment(ref version);
                    }

                    t.Update(entry.Quantity, time, updateId);
                }
            }
        }
    }

    private void UpdateAsks(PooledList<BinancePriceQuantityEntry<double>> asks, DateTimeOffset dateTime, long updateId)
    {
        if (asks.Count == 0)
        {
            return;
        }

        lock (_asks)
        {
            UpdatePart(_asks, asks, dateTime, updateId, ref _asksVersion);
        }
    }

    private void UpdateBids(PooledList<BinancePriceQuantityEntry<double>> bids, DateTimeOffset dateTime, long updateId)
    {
        if (bids.Count == 0)
        {
            return;
        }

        lock (_bids)
        {
            UpdatePart(_bids, bids, dateTime, updateId, ref _bidsVersion);
        }
    }

    public void Update(in BinanceHttpOrderbook orderbook, DateTimeOffset dateTime)
    {
        UpdateAsks(orderbook.Asks, dateTime, orderbook.LastUpdateId);
        UpdateBids(orderbook.Bids, dateTime, orderbook.LastUpdateId);
        LastUpdateId = orderbook.LastUpdateId;
    }

    public void Update(in DepthUpdateMessage updateMessage)
    {
        UpdateAsks(updateMessage.Asks, updateMessage.DateTimeOffset, updateMessage.u);
        UpdateBids(updateMessage.Bids, updateMessage.DateTimeOffset, updateMessage.u);
        LastUpdateId = Math.Max(updateMessage.u, updateMessage.U);
    }

    public (int askCount, int bidCount) DropOutdated(long updateId, double minBuy = double.MinValue,
        double maxSell = double.MaxValue)
    {
        if (minBuy > maxSell)
        {
            throw new ArgumentOutOfRangeException(nameof(minBuy), minBuy, "min is greater than max");
        }

        if (maxSell <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSell), maxSell, "maxSell is negative");
        }

        static int Drop(in ConcurrentDictionary<PriceRoundKey, T> dictionary, long updateId, PriceRoundKey roundMin,
            PriceRoundKey roundMax, ref long versionCounter)
        {
            Debug.Assert(roundMin.Value <= roundMax.Value, "roundMin <= roundMax");
            using PooledList<PriceRoundKey> toDrop = new();
            foreach (var (key, bookEntry) in dictionary)
            {
                if (bookEntry.UpdateId <= updateId && roundMin <= key && key <= roundMax)
                {
                    toDrop.Add(key);
                }
            }

            if (toDrop.Count > 0)
            {
                unchecked
                {
                    Interlocked.Increment(ref versionCounter);
                }
            }

            var res = 0;
            // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
            foreach (var key in toDrop)
            {
                res += dictionary.TryRemove(key, out _) ? 1 : 0;
            }

            return res;
        }

        var roundMin = minBuy > 0 ? PriceRoundKey.CreateFromPrice(minBuy) : new PriceRoundKey(double.MinValue);
        Debug.Assert(maxSell > 0, "maxSell > 0");
        var roundMax = PriceRoundKey.CreateFromPrice(maxSell);

        var askCount = Drop(in _asks, updateId, roundMin, roundMax, ref _asksVersion);
        var bidCount = Drop(in _bids, updateId, roundMin, roundMax, ref _bidsVersion);
        return (askCount, bidCount);
    }

    public (int askCount, int bidCount) DropOutdated(in BinanceHttpOrderbook orderbook)
    {
        static double PriceSelector(BinancePriceQuantityEntry<double> entry) => entry.Price;
        return DropOutdated(orderbook.LastUpdateId, PriceSelector(orderbook.Bids.MinBy(PriceSelector)),
            PriceSelector(orderbook.Asks.MaxBy(PriceSelector)));
    }

    public (int askCount, int bidCount) DropOutdated(in DateTimeOffset minDate)
    {
        static int Drop(in ConcurrentDictionary<PriceRoundKey, T> dictionary, in DateTimeOffset minDate,
            ref long version)
        {
            ArrayList<PriceRoundKey> toRemove = new();
            foreach (var (key, value) in dictionary)
            {
                if (value.Time < minDate)
                {
                    toRemove.Add(key);
                }
            }

            var res = toRemove.Count;

            if (res <= 0)
            {
                return 0;
            }


            Interlocked.Increment(ref version);
            lock (dictionary)
            {
                foreach (var key in toRemove)
                {
                    if (dictionary.TryRemove(key, out var value))
                    {
                        value.ResetStatistics();
                    }
                }
            }


            return res;
        }

        return (Drop(_asks, minDate, ref _asksVersion), Drop(_bids, minDate, ref _bidsVersion));
    }

    public bool IsEmpty() => _asks.IsEmpty && _bids.IsEmpty;
}