namespace Cryptodd.Binance.Http.Options;

public class
    BinancePublicHttpApiCallOptions : BaseBinancePublicHttpApiCallOptionsWithEndPoint<BinancePublicHttpApiEndPoint>
{
    public BinancePublicHttpApiCallOptions()
    {
        EndPoint = BinancePublicHttpApiEndPoint.None;
    }
}