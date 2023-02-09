using Cryptodd.Mmap;
using Cryptodd.Okx.Collectors.Options;

namespace Cryptodd.Okx.Collectors.RubikStat;

public struct OkxRubikDataDoubleSerializerConverter : IDoubleSerializerConverter<OkxRubikDataContext, RubikStatData>
{
    public RubikStatData Convert(in OkxRubikDataContext doubleSerializable) =>
        new(
            Timestamp: DateTimeOffset.Now.ToUnixTimeMilliseconds(),
            SellTakerVolumeSpot: doubleSerializable.Item2.SellVolume,
            BuyTakerVolumeSpot: doubleSerializable.Item2.BuyVolume,
            SellTakerVolumeContracts: doubleSerializable.Item3.SellVolume,
            BuyTakerVolumeContracts: doubleSerializable.Item3.BuyVolume,
            MarginLendingRatio: doubleSerializable.Item4.Ratio,
            LongShortRatio: doubleSerializable.Item5.Ratio,
            OpenInterest: doubleSerializable.Item6.OpenInterest,
            Volume: doubleSerializable.Item6.Volume
        );
}