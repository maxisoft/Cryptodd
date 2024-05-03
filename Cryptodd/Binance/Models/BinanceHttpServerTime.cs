namespace Cryptodd.Binance.Models;

// ReSharper disable once InconsistentNaming
public readonly record struct BinanceHttpServerTime(long serverTime)
{
    public DateTimeOffset DateTimeOffset => DateTimeOffset.FromUnixTimeMilliseconds(serverTime);
}