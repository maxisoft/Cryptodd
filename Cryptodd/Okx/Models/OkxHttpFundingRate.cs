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