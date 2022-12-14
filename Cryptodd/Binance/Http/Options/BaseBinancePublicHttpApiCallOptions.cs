using System.Text.Json;

namespace Cryptodd.Binance.Http.Options;

public interface IBinancePublicHttpApiCallOptions
{
    public int BaseWeight { get; }
    public string Url { get; }
    public JsonSerializerOptions? JsonSerializerOptions { get; }

    public int ComputeWeight(double factor);
}

public abstract class BaseBinancePublicHttpApiCallOptionsWithEndPoint<TEndPoint>: IBinancePublicHttpApiCallOptions where TEndPoint : struct, Enum
{
    public TEndPoint EndPoint { get; set; }
    public int BaseWeight { get; set; } = -1;
    public string Url { get; set; } = "";
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }

    public virtual int ComputeWeight(double factor) => BaseWeight;
}