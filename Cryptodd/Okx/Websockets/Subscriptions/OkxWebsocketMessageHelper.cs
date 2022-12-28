namespace Cryptodd.Okx.Websockets.Subscriptions;

public static class OkxWebsocketMessageHelper
{
    public static readonly ReadOnlyMemory<byte> PingMessage = "ping"u8.ToArray();
    public static readonly ReadOnlyMemory<byte> PongMessage = "pong"u8.ToArray();
}