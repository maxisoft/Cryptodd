using Cryptodd.Binance.Http.Options;

namespace Cryptodd.Binance.Http;

public class BinancePublicHttpApiCallKlinesOptions : BinancePublicHttpApiCallOptions
{
    public const string DefaultUrl = "/api/v3/klines";

    public BinancePublicHttpApiCallKlinesOptions()
    {
        EndPoint = BinancePublicHttpApiEndPoint.Kline;
        BaseWeight = 1;
        Url = DefaultUrl;
    }
}