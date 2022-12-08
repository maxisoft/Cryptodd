namespace Cryptodd.Binance.Models;

// ReSharper disable InconsistentNaming
public record struct CombinedStreamEnvelope<T>(string steam, T data) : IDisposable
    // ReSharper restore InconsistentNaming
{
    public T Data => data;
    public void Dispose()
    {
        if (Data is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}