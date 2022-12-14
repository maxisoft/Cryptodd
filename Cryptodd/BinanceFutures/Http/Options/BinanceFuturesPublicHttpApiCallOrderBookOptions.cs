namespace Cryptodd.BinanceFutures.Http.Options;

public class BinanceFuturesPublicHttpApiCallOrderBookOptions : BinanceFuturesPublicHttpApiCallOptions
{
    public const string DefaultUrl = "/fapi/v1/depth";

    public BinanceFuturesPublicHttpApiCallOrderBookOptions()
    {
        EndPoint = BinanceFuturesPublicHttpApiEndPoint.OrderBook;
        BaseWeight = -1;
        Url = DefaultUrl;
    }

    public override int ComputeWeight(double factor)
    {
        var weight = BaseWeight;
        if (weight >= 0)
        {
            return weight;
        }

        weight += 1;
        weight += (int)factor switch
        {
            >= 1000 => 50,
            >= 500 => 10,
            >= 100 => 5,
            >= 50 => 2,
            _ => -1
        };
        return weight;
    }
}