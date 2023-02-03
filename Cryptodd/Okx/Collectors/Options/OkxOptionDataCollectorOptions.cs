namespace Cryptodd.Okx.Collectors.Options;

public class OkxOptionDataCollectorOptions
{
    public HashSet<string> Underlyings { get; set; } = new() { "BTC-USD", "ETH-USD" };

    public int NumberOfOptionToPick { get; set; } = 32;
}