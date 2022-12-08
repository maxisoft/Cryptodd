namespace Cryptodd.Binance.Models;

// ReSharper disable InconsistentNaming
public record struct CombinedStreamEnvelope<T>(string Stream, T Data) : IDisposable
    // ReSharper restore InconsistentNaming
{
    public void Dispose()
    {
        if (Data is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}