namespace Cryptodd.Binance.Http.RateLimiter;

public interface IBinanceRateLimiter
{
    public const int DefaultMaxUsableWeight = 1200;
    int AvailableWeight { get; }
    long MaxUsableWeight { get; }
    ValueTask<IApiCallRegistration> WaitForSlot(Uri uri, int weight, CancellationToken cancellationToken);
}