namespace Cryptodd.TradeAggregates;

public class TradePeriodOptimizerOptions
{
    public TradePeriodOptimizerOptions()
    {
        DefaultApiPeriod = MinApiPeriod;
    }

    public TimeSpan MaxApiPeriod { get; set; } = TimeSpan.FromDays(7);
    public TimeSpan MinApiPeriod { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan DefaultApiPeriod { get; set; }

    public double Multiplier { get; set; } = 1.1;
    public int CapacityDivisor { get; set; } = 3;
}