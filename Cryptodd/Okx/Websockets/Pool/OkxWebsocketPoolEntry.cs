namespace Cryptodd.Okx.Websockets.Pool;

internal sealed class OkxWebsocketPoolEntry : IDisposable, IAsyncDisposable
{
    internal required PooledOkxWebsocket Websocket { get; init; }
    internal Task ActivityTask { get; set; } = Task.CompletedTask;

    internal required CancellationTokenSource CancellationTokenSource { get; init; }

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource.Cancel();
        if (!ActivityTask.IsCompleted)
        {
            try
            {
                await ActivityTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            
        }
        await Websocket.DisposeAsync().ConfigureAwait(false);

        ActivityTask.Dispose();
        ActivityTask = Task.CompletedTask;
        CancellationTokenSource.Dispose();
    }

    public void Dispose()
    {
        CancellationTokenSource.Cancel();
        if (!ActivityTask.IsCompleted)
        {
            try
            {
                ActivityTask.Wait();
            }
            catch (OperationCanceledException)
            {
            }
        }

        Websocket.Dispose();
        ActivityTask.Dispose();
        ActivityTask = Task.CompletedTask;
        CancellationTokenSource.Dispose();
    }
}