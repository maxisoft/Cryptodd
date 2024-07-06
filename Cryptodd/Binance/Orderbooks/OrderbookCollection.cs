using System.Collections;
using System.Collections.Concurrent;

namespace Cryptodd.Binance.Orderbooks;

public class OrderbookCollection : IReadOnlyCollection<string>
{
    private readonly ConcurrentDictionary<string, InMemoryOrderbook<OrderBookEntryWithStat>> _orderbooksPerSymbol = new();

    public InMemoryOrderbook<OrderBookEntryWithStat> this[string symbol] =>
        _orderbooksPerSymbol.GetOrAdd(symbol, ValueFactory);

    public int Count => _orderbooksPerSymbol.Count;

    public bool ContainsSymbol(string symbol)
    {
        return _orderbooksPerSymbol.ContainsKey(symbol);
    }

    private InMemoryOrderbook<OrderBookEntryWithStat> ValueFactory(string arg)
    {
        return new InMemoryOrderbook<OrderBookEntryWithStat> { Name = arg };
    }

    public IEnumerator<string> GetEnumerator()
    {
        return _orderbooksPerSymbol.Keys.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}