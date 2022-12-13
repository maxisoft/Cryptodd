namespace Cryptodd.Binance.Models;

public readonly record struct BinancePriceQuantityEntry<T>(T Price, T Quantity) where T : unmanaged;