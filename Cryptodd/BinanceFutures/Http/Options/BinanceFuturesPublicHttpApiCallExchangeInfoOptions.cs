namespace Cryptodd.BinanceFutures.Http.Options;

public class BinanceFuturesPublicHttpApiCallExchangeInfoOptions : BinanceFuturesPublicHttpApiCallOptions
{
    public const string DefaultUrl = "/fapi/v1/exchangeInfo";

    public BinanceFuturesPublicHttpApiCallExchangeInfoOptions()
    {
        EndPoint = BinanceFuturesPublicHttpApiEndPoint.ExchangeInfo;
        BaseWeight = 1;
        Url = DefaultUrl;
    }
}