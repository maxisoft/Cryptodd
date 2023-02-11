using System.Diagnostics;
using Cryptodd.IO;
using Cryptodd.IO.Mmap;

namespace Cryptodd.Okx.Collectors.Options;

public sealed record OkxOptionData(
    long Timestamp,
    double ExpiryTimeDiff,
    double Delta,
    double Gamma,
    double Vega,
    double Theta,
    double DeltaBs,
    double GammaBs,
    double VegaBs,
    double ThetaBs,
    double Lever,
    double MarkVol,
    double BidVol,
    double AskVol,
    double ForwardPrice,
    double OpenInterest,
    double SpreadPercent, // (ask / bid - 1) * 100
    double SpreadToMarkPercent,
    double Change24HPercent,
    double ChangeTodayPercent,
    double ChangeTodayChinaPercent,
    double Volume24H,
    double PriceRatio24H, // (price - low) / (high - low)
    double LastPrice,
    double Price
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
                p[1] = ExpiryTimeDiff;
                p[2] = Delta;
                p[3] = Gamma;
                p[4] = Vega;
                p[5] = Theta;
                p[6] = DeltaBs;
                p[7] = GammaBs;
                p[8] = VegaBs;
                p[9] = ThetaBs;
                p[10] = Lever;
                p[11] = MarkVol;
                p[12] = BidVol;
                p[13] = AskVol;
                p[14] = ForwardPrice;
                p[15] = OpenInterest;
                p[16] = SpreadPercent;
                p[17] = SpreadToMarkPercent;
                p[18] = Change24HPercent;
                p[19] = ChangeTodayPercent;
                p[20] = ChangeTodayChinaPercent;
                p[21] = Volume24H;
                p[22] = PriceRatio24H;
                p[23] = LastPrice;
                p[24] = Price;
            }
        }

        return size;
    }

    public int ExpectedSize => 25;
}