namespace Cryptodd.Okx.Models;

public readonly record struct OkxOrderbookEntry(double Price, double Quantity, int Count);