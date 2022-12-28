namespace Cryptodd.Binance.Http.RateLimiter;

public class BinanceRateLimiterOptions
{
    public int DefaultMaxUsableWeight { get; set; } = IBinanceRateLimiter.DefaultMaxUsableWeight;
    public float UsableMaxWeightMultiplier { get; set; } = 1.0f;

    public TimeSpan WaitForSlotTimeout { get; set; } = TimeSpan.FromMinutes(2);

    public float AvailableWeightMultiplier { get; set; } = 0.8f;
}