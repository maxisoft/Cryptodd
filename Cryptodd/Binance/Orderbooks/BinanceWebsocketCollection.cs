using Cryptodd.Binance.Orderbooks.Websockets;
using Maxisoft.Utils.Collections.Lists;

namespace Cryptodd.Binance.Orderbooks;

public sealed class BinanceWebsocketCollection : BaseBinanceWebsocketCollection<BinanceOrderbookWebsocket,
    BinanceOrderbookWebsocketOptions>
{
    public static readonly BinanceWebsocketCollection Empty = new();
    public static BinanceWebsocketCollection Create(ArrayList<string> symbols,
        Func<BinanceOrderbookWebsocket> factory) =>
        DoCreate<BinanceWebsocketCollection>(symbols, factory);
}