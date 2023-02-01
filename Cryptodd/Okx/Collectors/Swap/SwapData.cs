using System.Diagnostics;
using Cryptodd.Mmap;

namespace Cryptodd.Okx.Collectors.Swap;

public readonly record struct SwapData(
    long Timestamp,
    long NextFundingTime,
    double FundingRate,
    double NextFundingRate,
    double OpenInterest,
    double OpenInterestInCurrency
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
            }
        }

        return size;
    }

    public int ExpectedSize => 6;
}