using Cryptodd.Okx.Models;
using Cryptodd.Okx.Models.RubikStats;
using Cryptodd.Okx.Options;

namespace Cryptodd.Okx.Collectors.Options;

public sealed class OkxRubikDataContext : Tuple<string, OkxHttpRubikTakerVolume, OkxHttpRubikTakerVolume,
    OkxHttpRubikMarginLendingRatio,
    OkxHttpRubikLongShortRatio,
    OkxHttpRubikOpenInterestVolume>
{
    public OkxRubikDataContext(string item1, OkxHttpRubikTakerVolume item2, OkxHttpRubikTakerVolume item3,
        OkxHttpRubikMarginLendingRatio item4, OkxHttpRubikLongShortRatio item5, OkxHttpRubikOpenInterestVolume item6) :
        base(item1, item2, item3, item4, item5, item6) { }

    public sealed class OkxRubikDataContextEqualityComparer : IEqualityComparer<OkxRubikDataContext>
    {
        public bool Equals(OkxRubikDataContext x, OkxRubikDataContext y)
        {
            var eq = x.Item1.Equals(y.Item1);
            if (!eq) return eq;
            eq = x.Item2.Timestamp.Equals(y.Item2.Timestamp);
            if (!eq) return eq;
            eq = x.Item3.Timestamp.Equals(y.Item3.Timestamp);
            if (!eq) return eq;
            eq = x.Item4.Timestamp.Equals(y.Item4.Timestamp);
            if (!eq) return eq;
            eq = x.Item5.Timestamp.Equals(y.Item5.Timestamp);
            if (!eq) return eq;
            eq = x.Item6.Timestamp.Equals(y.Item6.Timestamp);
            return eq;
        }

        public int GetHashCode(OkxRubikDataContext obj)
        {
            HashCode hashCode = new();
            hashCode.Add(obj.Item1);
            hashCode.Add(obj.Item2.Timestamp);
            hashCode.Add(obj.Item3.Timestamp);
            hashCode.Add(obj.Item4.Timestamp);
            hashCode.Add(obj.Item5.Timestamp);
            hashCode.Add(obj.Item6.Timestamp);
            return hashCode.ToHashCode();
        }
    }

    public static OkxRubikDataContextEqualityComparer TimestampComparer { get; } =
        new OkxRubikDataContextEqualityComparer();
}