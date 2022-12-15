namespace Cryptodd.Binance.Http.Options;

public class BinancePublicHttpApiOptions : BaseBinancePublicHttpApiOptions
{
    public const string DefaultBaseAddress = "https://api.binance.com";

    public BinancePublicHttpApiOptions()
    {
        if (string.IsNullOrEmpty(BaseAddress))
        {
            BaseAddress = DefaultBaseAddress;
        }
    }
}