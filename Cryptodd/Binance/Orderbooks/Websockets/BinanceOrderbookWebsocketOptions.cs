namespace Cryptodd.Binance.Orderbooks.Websockets;

public sealed class BinanceOrderbookWebsocketOptions : BaseBinanceOrderbookWebsocketOptions
{
    public const string DefaultBaseAddress = "wss://stream.binance.com:443";

    public BinanceOrderbookWebsocketOptions()
    {
        if (string.IsNullOrEmpty(BaseAddress))
        {
            BaseAddress = DefaultBaseAddress;
        }
    }
}