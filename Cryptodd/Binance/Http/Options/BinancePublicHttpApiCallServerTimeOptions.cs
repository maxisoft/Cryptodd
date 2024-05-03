namespace Cryptodd.Binance.Http.Options;

public class BinancePublicHttpApiCallServerTimeOptions : BinancePublicHttpApiCallOptions
{
    public BinancePublicHttpApiCallServerTimeOptions()
    {
        Url = "/api/v3/time";
        EndPoint = BinancePublicHttpApiEndPoint.ServerTime;
        BaseWeight = 1;
    }
}