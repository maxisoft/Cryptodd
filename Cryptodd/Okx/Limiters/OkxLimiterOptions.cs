namespace Cryptodd.Okx.Limiters;

public class OkxLimiterOptions
{
    public int? MaxLimit { get; set; }
    public TimeSpan? Period { get; set; }
    public int? TickPollingTimer { get; set; }
}