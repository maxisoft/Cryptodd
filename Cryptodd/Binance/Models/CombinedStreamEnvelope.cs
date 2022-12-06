namespace Cryptodd.Binance.Models;

public record struct CombinedStreamEnvelope<T>(string Steam, T Data) : IDisposable
{
    public void Dispose()
    {
        if (Data is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}