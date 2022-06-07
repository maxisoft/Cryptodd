namespace Cryptodd.Ftx.Models;

public readonly record struct FtxTrade(long Id, double Price, double Size, FtxTradeFlag Flag, DateTimeOffset Time)
{
    public string Side => Flag.HasFlag(FtxTradeFlag.Buy) ? "buy" : (Flag.HasFlag(FtxTradeFlag.Sell) ? "sell" : "none");
    public bool IsBuy => Flag.HasFlag(FtxTradeFlag.Buy);
    public bool IsSell => Flag.HasFlag(FtxTradeFlag.Sell);
    public bool IsLiquidation => Flag.HasFlag(FtxTradeFlag.Liquidation);
}