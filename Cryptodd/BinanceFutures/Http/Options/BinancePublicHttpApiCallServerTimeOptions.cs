using Cryptodd.Binance.Http;
using Cryptodd.Binance.Http.Options;

namespace Cryptodd.BinanceFutures.Http.Options;

public class BinanceFuturesPublicHttpApiCallServerTimeOptions : BinanceFuturesPublicHttpApiCallOptions
{
    public BinanceFuturesPublicHttpApiCallServerTimeOptions()
    {
        Url = "/fapi/v1/time";
        EndPoint = BinanceFuturesPublicHttpApiEndPoint.ServerTime;
        BaseWeight = 1;
    }
}