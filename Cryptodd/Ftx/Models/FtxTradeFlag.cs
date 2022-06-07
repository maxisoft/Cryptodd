namespace Cryptodd.Ftx.Models;

[Flags]
public enum FtxTradeFlag : byte
{
    None = 0,
    Buy = 1,
    Sell = 1 << 1,
    Liquidation = 1 << 2
}