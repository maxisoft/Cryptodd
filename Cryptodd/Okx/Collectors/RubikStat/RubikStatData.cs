using System.Diagnostics;
using Cryptodd.Mmap;

namespace Cryptodd.Okx.Collectors.RubikStat;

public record RubikStatData(
    long Timestamp,
    double SellTakerVolumeSpot,
    double BuyTakerVolumeSpot,
    double SellTakerVolumeContracts,
    double BuyTakerVolumeContracts,
    double MarginLendingRatio,
    double LongShortRatio,
    double OpenInterest,
    double Volume
): IDoubleSerializable
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
                p[1] = SellTakerVolumeSpot;
                p[2] = BuyTakerVolumeSpot;
                p[3] = SellTakerVolumeContracts;
                p[4] = BuyTakerVolumeContracts;
                p[5] = MarginLendingRatio;
                p[6] = LongShortRatio;
                p[7] = OpenInterest;
                p[8] = Volume;
            }
        }

        return size;
    }

    public int ExpectedSize => 9;
}