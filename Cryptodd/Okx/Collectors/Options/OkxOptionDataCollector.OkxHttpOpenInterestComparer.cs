using Cryptodd.Okx.Models;
using Cryptodd.Okx.Options;

namespace Cryptodd.Okx.Collectors.Options;

public partial class OkxOptionDataCollector
{
    private struct
        OkxHttpOpenInterestComparer : IComparer<(OkxHttpOpenInterest, OkxOptionInstrumentId, OkxHttpTickerInfo)>
    {
        public OkxHttpOpenInterestComparer() { }

        public OkxHttpOpenInterestComparer(bool prefer24HVolume)
        {
            Prefer24HVolume = prefer24HVolume;
        }

        public DateTime Now { get; init; } = DateTime.Today;

        public bool Prefer24HVolume { get; }

        public int Compare((OkxHttpOpenInterest, OkxOptionInstrumentId, OkxHttpTickerInfo) x,
            (OkxHttpOpenInterest, OkxOptionInstrumentId, OkxHttpTickerInfo) y)
        {
            if (x.Item1.oi == 0)
            {
                if (y.Item1.oi == 0)
                {
                    return OptionInstrumentIdComparison(in x.Item2, in y.Item2);
                }

                return -1;
            }

            if (y.Item1.oi == 0)
            {
                return 1;
            }

            static int LogScale(double value)
            {
                return (int)Math.Log10(Math.Max(value, 1));
            }

            int cmp;

            int VolumeComparison()
            {
                return LogScale(x.Item3.vol24h).CompareTo(LogScale(y.Item3.vol24h));
            }

            if (Prefer24HVolume)
            {
                cmp = VolumeComparison();
                if (cmp != 0)
                {
                    return cmp;
                }
            }

            static double GetScore(in OkxHttpOpenInterest openInterest, in OkxOptionInstrumentId instrumentId,
                DateTime now)
            {
                return openInterest.oi / (
                    Math.Log2(
                        Math.Max((instrumentId.Date.ToDateTime(default) - now).TotalDays, 1))
                    + 1e-3
                );
            }

            cmp = GetScore(in x.Item1, in x.Item2, Now).CompareTo(GetScore(in y.Item1, in y.Item2, Now));

            if (cmp != 0)
            {
                return cmp;
            }

            // ReSharper disable once InvertIf
            if (!Prefer24HVolume)
            {
                cmp = VolumeComparison();
                if (cmp != 0)
                {
                    return cmp;
                }
            }

            return OptionInstrumentIdComparison(in x.Item2, in y.Item2);
        }
    }
}