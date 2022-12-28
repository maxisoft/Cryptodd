namespace Cryptodd.Binance.Http.Options;

public class BinancePublicHttpApiOptions : BaseBinancePublicHttpApiOptions
{
    public const string DefaultBaseAddress = "https://api.binance.com";
    public const string DefaultUsaBaseAddress = "https://api.binance.us";

    public BinancePublicHttpApiOptions()
    {
        if (string.IsNullOrEmpty(BaseAddress))
        {
            BaseAddress = DefaultBaseAddress;
        }
    }

    public override bool ChangeAddressToUSA()
    {
        if (string.IsNullOrEmpty(BaseAddress) || BaseAddress == DefaultBaseAddress)
        {
            BaseAddress = DefaultUsaBaseAddress;
            return true;
        }

        return false;
    }
}