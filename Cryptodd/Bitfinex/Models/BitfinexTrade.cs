namespace Cryptodd.Bitfinex.Models;

public readonly record struct BitfinexTrade(long Id, long Mts, double Amount, double Price)
{
    public DateTimeOffset DateTime => DateTimeOffset.FromUnixTimeMilliseconds(Mts);
}