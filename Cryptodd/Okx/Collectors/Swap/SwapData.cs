using System.Diagnostics;
using Cryptodd.IO;
using Cryptodd.IO.Mmap;

namespace Cryptodd.Okx.Collectors.Swap;

public readonly record struct SwapData(
    long Timestamp,
    long NextFundingTime,
    double FundingRate,
    double NextFundingRate,
    double OpenInterest,
    double OpenInterestInCurrency,
    double SpreadPercent, // (ask / bid - 1) * 100
    double SpreadToMarkPercent,
    double AskSize,
    double BidSize,
    double Change24HPercent,
    double ChangeTodayPercent,
    double ChangeTodayChinaPercent,
    double Volume24H,
    double PriceRatio24H, // (price - low) / (high - low)
    double LastPrice
) : IDoubleSerializable
{
    public int WriteTo(Span<double> buffer)
    {
        var size = ExpectedSize;
        Debug.Assert(buffer.Length >= size);
        unsafe
        {
            fixed (double* p = buffer)
            {
                p[0] = Timestamp;
                p[1] = NextFundingTime;
                p[2] = FundingRate;
                p[3] = NextFundingRate;
                p[4] = OpenInterest;
                p[5] = OpenInterestInCurrency;
                p[6] = SpreadPercent;
                p[7] = SpreadToMarkPercent;
                p[8] = AskSize;
                p[9] = BidSize;
                p[10] = Change24HPercent;
                p[11] = ChangeTodayPercent;
                p[12] = ChangeTodayChinaPercent;
                p[13] = Volume24H;
                p[14] = PriceRatio24H;
                p[15] = LastPrice;
            }
        }

        return size;
    }

    public int ExpectedSize => 16;
}