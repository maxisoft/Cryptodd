using Maxisoft.Utils.Algorithms;

namespace Cryptodd.TradeAggregates;

public class TradeCollectorOptions
{
    public int MaxParallelism { get; set; }
    public int Timeout { get; set; } = 60 * 1000;
    public string PairFilterName { get; set; } = "Trade";

    public bool LockTable { get; set; } = true;

    public long MinimumDate { get; set; } = 0;

    public TradeCollectorOptions()
    {
        MaxParallelism = Environment.ProcessorCount.Clamp(2, 32);
    }
}