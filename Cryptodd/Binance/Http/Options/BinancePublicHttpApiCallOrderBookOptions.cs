using Cryptodd.Binance.Http.Options;

namespace Cryptodd.Binance.Http;

public class BinancePublicHttpApiCallOrderBookOptions : BinancePublicHttpApiCallOptions
{
    public const string DefaultUrl = "/api/v3/depth";

    public BinancePublicHttpApiCallOrderBookOptions()
    {
        EndPoint = BinancePublicHttpApiEndPoint.OrderBook;
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
            > 1000 => 50,
            > 500 => 10,
            > 100 => 5,
            _ => -1
        };
        return weight;
    }
}