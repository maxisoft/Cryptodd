namespace Cryptodd.Binance.Http.Options;

public class BinancePublicHttpApiCallExchangeInfoOptions : BinancePublicHttpApiCallOptions
{
    public const string DefaultUrl = "/api/v3/exchangeInfo";
    public const int DefaultBaseWeight = 10;

    public BinancePublicHttpApiCallExchangeInfoOptions()
    {
        EndPoint = BinancePublicHttpApiEndPoint.ExchangeInfo;
        BaseWeight = DefaultBaseWeight;
        Url = DefaultUrl;
    }
}