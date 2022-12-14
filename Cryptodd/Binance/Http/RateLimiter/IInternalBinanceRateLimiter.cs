namespace Cryptodd.Binance.Http.RateLimiter;

public interface IInternalBinanceRateLimiter : IBinanceRateLimiter
{
    BinanceRateLimiterOptions Options { get; }
    new long MaxUsableWeight { get; set; }

    float AvailableWeightMultiplier { get; set; }
    void UpdateUsedWeightFromBinance(int weight, DateTimeOffset dateTimeOffset);
    void UpdateUsedWeightFromBinance(int weight) => UpdateUsedWeightFromBinance(weight, DateTimeOffset.Now);
}