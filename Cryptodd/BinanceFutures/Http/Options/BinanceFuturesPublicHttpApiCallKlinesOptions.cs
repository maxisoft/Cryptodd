namespace Cryptodd.BinanceFutures.Http.Options;

public class BinanceFuturesPublicHttpApiCallKlinesOptions : BinanceFuturesPublicHttpApiCallOptions
{
    public const string DefaultUrl = "/fapi/v1/klines";

    public BinanceFuturesPublicHttpApiCallKlinesOptions()
    {
        EndPoint = BinanceFuturesPublicHttpApiEndPoint.Kline;
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
            >= 1000 => 10,
            >= 500 => 5,
            >= 100 => 2,
            _ => 1
        };
        return weight;
    }
}