using Cryptodd.Websockets;

namespace Cryptodd.Binance.Orderbooks.Websockets;

public abstract class BaseBinanceOrderbookWebsocketOptions : BaseWebsocketOptions
{
    public int MaxStreamCountSoftLimit { get; set; } = 512;
}