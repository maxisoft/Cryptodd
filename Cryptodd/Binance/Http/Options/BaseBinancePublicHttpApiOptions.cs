namespace Cryptodd.Binance.Http.Options;

public abstract class BaseBinancePublicHttpApiOptions
{
    public string BaseAddress { get; set; } = "";
    public float UsedWeightMultiplier { get; set; } = 1.0f;
    public string UsedWeightHeaderName { get; set; } = "X-MBX-USED-WEIGHT-1M";

    public abstract bool ChangeAddressToUSA();
}