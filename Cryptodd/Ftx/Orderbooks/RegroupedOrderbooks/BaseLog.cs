namespace Cryptodd.Ftx.Orderbooks.RegroupedOrderbooks;

public interface IBaseLog
{
    float Log(float x);
    float Exp(float x);
}

public struct BaseLog2 : IBaseLog
{
    public float Log(float x) => MathF.Log2(x);

    public float Exp(float x) => MathF.Pow(2, x);
}