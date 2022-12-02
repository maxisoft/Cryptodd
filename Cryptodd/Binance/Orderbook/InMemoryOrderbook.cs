using Cryptodd.Binance.Models;
using Maxisoft.Utils.Collections.Lists.Specialized;
using Towel;
using Towel.DataStructures;

namespace Cryptodd.Binance.Orderbook;

public partial class InMemoryOrderbook<T> where T : IOrderBookEntry, new()
{
    private readonly Dictionary<PriceRoundKey, T> _asks = new();
    private long _asksVersion;
    private readonly Dictionary<PriceRoundKey, T> _bids = new();
    private long _bidsVersion;

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

    static void UpdatePart(
        Dictionary<PriceRoundKey, T> dictionary,
        PooledList<BinancePriceQuantityEntry<double>> update,
        DateTimeOffset time,
        ref long version)
    {
        foreach (var entry in update)
        {
            var key = PriceRoundKey.CreateFromPrice(entry.Price);
            if (dictionary.TryGetValue(key, out var prev))
            {
                // ReSharper disable once InvertIf
                if (time > prev.Time)
                {
                    if (entry.Quantity <= 0 && prev.ChangeCounter == 0)
                    {
                        dictionary.Remove(key);
                        // ReSharper disable once RedundantOverflowCheckingContext
                        unchecked
                        {
                            Interlocked.Increment(ref version);
                        }
                    }
                    else
                    {
                        prev.Update(entry.Quantity, time);
                    }
                }
            }
            else
            {
                var t = new T() { Price = entry.Price, Quantity = entry.Quantity, Time = time };
                // ReSharper disable once InvertIf
                if (dictionary.TryAdd(key, t))
                {
                    // ReSharper disable once RedundantOverflowCheckingContext
                    unchecked
                    {
                        Interlocked.Increment(ref version);
                    }

                    t.Update(entry.Quantity, time);
                }
            }
        }
    }

    private void UpdateAsks(PooledList<BinancePriceQuantityEntry<double>> asks, DateTimeOffset dateTime)
    {
        lock (_asks)
        {
            UpdatePart(_asks, asks, dateTime, ref _asksVersion);
        }
    }
    
    private void UpdateBids(PooledList<BinancePriceQuantityEntry<double>> bids, DateTimeOffset dateTime)
    {
        lock (_bids)
        {
            UpdatePart(_bids, bids, dateTime, ref _bidsVersion);
        }
    }

    public void Update(in BinanceHttpOrderbook orderbook, DateTimeOffset dateTime)
    {
        UpdateAsks(orderbook.Asks, dateTime);
        UpdateBids(orderbook.Bids, dateTime);
    }

    public void Update(in DepthUpdateMessage updateMessage)
    {
        UpdateAsks(updateMessage.Asks, updateMessage.DateTimeOffset);
        UpdateBids(updateMessage.Bids, updateMessage.DateTimeOffset);
    }
}