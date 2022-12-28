namespace Cryptodd.Okx.Websockets.Pool;

internal sealed class OkxWebsocketPoolEntry : IDisposable, IAsyncDisposable
{
    internal required PooledOkxWebsocket Websocket { get; init; }
    internal Task ActivityTask { get; set; } = Task.CompletedTask;

    internal required CancellationTokenSource CancellationTokenSource { get; init; }

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource.Cancel();
        await ActivityTask.ConfigureAwait(false);
        CancellationTokenSource.Dispose();
        await Websocket.DisposeAsync();
        ActivityTask.Dispose();
    }

    public void Dispose()
    {
        CancellationTokenSource.Cancel();
        ActivityTask.Wait();
        Websocket.Dispose();
        ActivityTask.Dispose();
        CancellationTokenSource.Dispose();
    }
}