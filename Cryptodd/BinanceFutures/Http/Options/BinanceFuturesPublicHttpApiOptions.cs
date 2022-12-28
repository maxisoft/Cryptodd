using Cryptodd.Binance.Http.Options;

namespace Cryptodd.BinanceFutures.Http.Options;

public class BinanceFuturesPublicHttpApiOptions : BaseBinancePublicHttpApiOptions
{
    public const string DefaultBaseAddress = "https://fapi.binance.com";
    public const string DefaultUsaBaseAddress = "https://fapi.binance.com";

    public BinanceFuturesPublicHttpApiOptions()
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