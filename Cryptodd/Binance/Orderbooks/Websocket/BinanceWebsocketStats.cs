using System.Collections.Concurrent;

namespace Cryptodd.Binance.Orderbooks.Websocket;

public readonly record struct PerSymbolStatsEntry(string Symbol, long CallCounter, DateTimeOffset LastCall) { }

public class BinanceWebsocketStats
{
    private ConcurrentDictionary<string, PerSymbolStatsEntry> _perSymbolStats =
        new ConcurrentDictionary<string, PerSymbolStatsEntry>();

    private PerSymbolStatsEntry _globalSymbol = new PerSymbolStatsEntry("", 0, DateTimeOffset.Now);

    public long CallCounter => _globalSymbol.CallCounter;
    public DateTimeOffset LastCall => _globalSymbol.LastCall;

    private static bool IsGlobal(string s) => string.IsNullOrEmpty(s);

    public void RegisterTick() => RegisterSymbol("");

    public void RegisterSymbol(string symbol)
    {
        if (IsGlobal(symbol))
        {
            _globalSymbol = UpdateValueFactory(symbol, _globalSymbol);
            return;
        }

        _perSymbolStats.AddOrUpdate(symbol, AddValueFactory, UpdateValueFactory);
    }

    public PerSymbolStatsEntry GetStatsForSymbol(string symbol)
    {
        if (IsGlobal(symbol))
        {
            return _globalSymbol;
        }

        if (!_perSymbolStats.TryGetValue(symbol, out var res))
        {
            res = AddValueFactory(symbol) with { LastCall = DateTimeOffset.UnixEpoch };
        }

        return res;
    }

    private static PerSymbolStatsEntry UpdateValueFactory(string symbol, PerSymbolStatsEntry entry) => entry with
    {
        CallCounter = entry.CallCounter + 1, LastCall = DateTimeOffset.Now
    };

    private static PerSymbolStatsEntry AddValueFactory(string symbol) =>
        new PerSymbolStatsEntry(symbol, 0, DateTimeOffset.Now);
}