using System.Text.Json.Nodes;
using Maxisoft.Utils.Collections.Lists;

namespace Cryptodd.Binance.Http;

public static class BinanceHttpApiHelper
{
    public static int ParseSymbols<TCollection>(ref TCollection acc, in JsonObject exchangeInfo,
        bool checkStatus = false) where TCollection : ICollection<string>
    {
        var res = 0;
        // ReSharper disable once InvertIf
        if (exchangeInfo["symbols"] is JsonArray symbols)
        {
            switch (acc)
            {
                case ArrayList<string> al:
                    al.EnsureCapacity(symbols.Count);
                    break;
                case List<string> l:
                    l.EnsureCapacity(symbols.Count);
                    break;
            }

            foreach (var symbolInfoNode in symbols)
            {
                if (symbolInfoNode is not JsonObject symbolInfo)
                {
                    continue;
                }

                // ReSharper disable once InvertIf
                if (symbolInfo["symbol"] is JsonValue symbol && (!checkStatus ||
                                                                 (symbolInfo["status"] is JsonValue status &&
                                                                  status.GetValue<string>() == "TRADING")))
                {
                    acc.Add(symbol.GetValue<string>());
                    res++;
                }
            }
        }

        return res;
    }
}