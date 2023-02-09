using Cryptodd.Mmap;
using Cryptodd.Okx.Collectors.Options;

namespace Cryptodd.Okx.Collectors.RubikStat;

public struct OkxRubikDataDoubleSerializerConverter : IDoubleSerializerConverter<OkxRubikDataContext, RubikStatData>
{
    public RubikStatData Convert(in OkxRubikDataContext doubleSerializable) =>
        new(
            DateTimeOffset.Now.ToUnixTimeMilliseconds(),
            doubleSerializable.Item2.SellVolume,
            doubleSerializable.Item2.BuyVolume,
            doubleSerializable.Item3.SellVolume,
            doubleSerializable.Item3.BuyVolume,
            doubleSerializable.Item4.Ratio,
            doubleSerializable.Item5.Ratio,
            doubleSerializable.Item6.OpenInterest,
            doubleSerializable.Item6.Volume
        );
}