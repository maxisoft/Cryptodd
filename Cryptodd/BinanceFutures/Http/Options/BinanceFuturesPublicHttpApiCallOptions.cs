using Cryptodd.Binance.Http.Options;

namespace Cryptodd.BinanceFutures.Http.Options;

public class
    BinanceFuturesPublicHttpApiCallOptions : BaseBinancePublicHttpApiCallOptionsWithEndPoint<
        BinanceFuturesPublicHttpApiEndPoint>
{
    public BinanceFuturesPublicHttpApiCallOptions()
    {
        EndPoint = BinanceFuturesPublicHttpApiEndPoint.None;
    }
}