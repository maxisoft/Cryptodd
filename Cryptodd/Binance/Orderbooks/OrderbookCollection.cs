using System.Collections;
using System.Collections.Concurrent;

namespace Cryptodd.Binance.Orderbooks;

public class OrderbookCollection : IReadOnlyCollection<string>
{
    private ConcurrentDictionary<string, InMemoryOrderbook<OrderBookEntryWithStat>> _orderbooksPerSymbol = new();

    public InMemoryOrderbook<OrderBookEntryWithStat> this[string symbol] =>
        _orderbooksPerSymbol.GetOrAdd(symbol, ValueFactory);

    public int Count => _orderbooksPerSymbol.Count;

    public bool ContainsSymbol(string symbol) => _orderbooksPerSymbol.ContainsKey(symbol);

    private InMemoryOrderbook<OrderBookEntryWithStat> ValueFactory(string arg) => new();

    public IEnumerator<string> GetEnumerator() => _orderbooksPerSymbol.Keys.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}