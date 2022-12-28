namespace Cryptodd.Okx.Websockets.Pool;

public class OkxWebsocketPoolOptions
{
    public int MaxCapacity { get; set; } = 20;

    public int BackgroundTaskInterval { get; set; } = 1500;
}