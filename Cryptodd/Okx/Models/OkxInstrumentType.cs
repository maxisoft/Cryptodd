namespace Cryptodd.Okx.Models;

public enum OkxInstrumentType: byte
{
    None = 0,
    Spot = 1,
    Margin = 1 << 1,
    Swap = 1 << 2,
    Futures = 1 << 3,
    Option = 1 << 4,
    Contracts = Margin | Swap | Futures
}