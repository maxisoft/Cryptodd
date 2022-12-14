using Cryptodd.Binance.Orderbooks;
using Maxisoft.Utils.Collections.Lists;

namespace Cryptodd.BinanceFutures.Orderbooks.Websockets;

public sealed class BinanceFuturesWebsocketCollection : BaseBinanceWebsocketCollection<BinanceFuturesOrderbookWebsocket,
    BinanceFuturesOrderbookWebsocketOptions>
{
    public static readonly BinanceFuturesWebsocketCollection Empty = new();
    public static BinanceFuturesWebsocketCollection Create(ArrayList<string> symbols,
        Func<BinanceFuturesOrderbookWebsocket> factory) =>
        DoCreate<BinanceFuturesWebsocketCollection>(symbols, factory);
}