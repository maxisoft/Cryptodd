namespace Cryptodd.Binance.Orderbook.Websocket;

public abstract class BaseBinanceOrderbookWebsocketOptions
{
    public string BaseAddress { get; set; } = "wss://stream.binance.com:443";
    public int ReceiveTimeout { get; set; } = 60_000;

    public int AdditionalReceiveBufferSize { get; set; } = 128 << 10;

    public int MaxStreamCountSoftLimit { get; set; } = 512;

    public int CloseConnectionTimeout { get; set; } = 1000;
}