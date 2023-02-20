namespace Cryptodd.Okx.Websockets.Pool;

public sealed class PooledOkxWebsocketOptions : BaseOkxWebsocketOptions
{
    public PooledOkxWebsocketOptions()
    {
        ReceiveTimeout = 1000;
    }
}