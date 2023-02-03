namespace Cryptodd.Okx.Collectors.Options;

public class OkxOptionDataCollectorOptions
{
    public HashSet<string> Underlyings { get; set; } = new() { "BTC-USD", "ETH-USD" };

    public int NumberOfOptionToPick { get; set; } = 32;
    
    public bool? SkipOnNoChange { get; set; }
    
    public bool? Prefer24HVolume { get; set; }
}