using System.Diagnostics.CodeAnalysis;
using Cryptodd.Json;

namespace Cryptodd.Okx.Models;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public record OkxHttpFundingRate(
    SafeJsonDouble<SafeJsonDoubleDefaultValue> fundingRate,
    JsonLong fundingTime,
    PooledString instId,
    PooledString instType,
    SafeJsonDouble<SafeJsonDoubleDefaultValue> nextFundingRate,
    JsonLong nextFundingTime
);

[SuppressMessage("ReSharper", "InconsistentNaming")]
public record OkxHttpFundingRateWithDate(
    SafeJsonDouble<SafeJsonDoubleDefaultValue> fundingRate,
    JsonLong fundingTime,
    PooledString instId,
    PooledString instType,
    SafeJsonDouble<SafeJsonDoubleDefaultValue> nextFundingRate,
    JsonLong nextFundingTime,
    DateTimeOffset date
) : OkxHttpFundingRate(fundingRate, fundingTime, instId, instType, nextFundingRate, nextFundingTime)
{
    public static OkxHttpFundingRateWithDate FromOkxHttpFundingRate(OkxHttpFundingRate fr) => new OkxHttpFundingRateWithDate(fr.fundingRate, fr.fundingTime, fr.instId, fr.instType, fr.nextFundingRate, fr.nextFundingTime, DateTimeOffset.Now);
}