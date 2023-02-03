using Cryptodd.Okx.Models;
using Cryptodd.Okx.Options;

namespace Cryptodd.Okx.Collectors.Options;

public partial class OkxOptionDataCollector
{
    private readonly struct OkxHttpOpenInterestComparer : IComparer<TopKData>
    {
        public OkxHttpOpenInterestComparer() { }

        public OkxHttpOpenInterestComparer(bool prefer24HVolume)
        {
            Prefer24HVolume = prefer24HVolume;
        }

        public DateTime Now { get; init; } = DateTime.Today;

        public bool Prefer24HVolume { get; }

        public int Compare(TopKData? x, TopKData? y)
        {
            if (x is null)
            {
                return y is null ? 0 : -1;
            }

            if (y is null)
            {
                return 1;
            }

            if (x.Item1.oi == 0)
            {
                if (y.Item1.oi == 0)
                {
                    return OptionInstrumentIdComparison(x.Item2, y.Item2);
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

            static double GetScore(OkxHttpOpenInterest openInterest, OkxOptionInstrumentId instrumentId,
                DateTime now)
            {
                return openInterest.oi / (
                    Math.Log2(
                        Math.Max((instrumentId.Date.ToDateTime(default) - now).TotalDays, 1))
                    + 1e-3
                );
            }

            cmp = GetScore(x.Item1, x.Item2, Now).CompareTo(GetScore(y.Item1, y.Item2, Now));

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

            return OptionInstrumentIdComparison(x.Item2, y.Item2);
        }
    }
}