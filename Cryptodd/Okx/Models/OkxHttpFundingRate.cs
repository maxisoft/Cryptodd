using System.Diagnostics.CodeAnalysis;
using Cryptodd.Json;

namespace Cryptodd.Okx.Models;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public record OkxHttpFundingRate(
    SafeJsonDouble<SafeJsonDoubleDefaultValue> fundingRate, // Current funding rate
    JsonLong fundingTime, // Settlement time, Unix timestamp format in milliseconds
    PooledString instId, // Instrument ID, e.g. BTC-USD-SWAP
    PooledString instType, // Instrument type, SWAP
    SafeJsonDouble<SafeJsonDoubleDefaultValue> nextFundingRate, // Forecasted funding rate for the next period
    JsonLong nextFundingTime, // Forecasted funding time for the next period, Unix timestamp format in milliseconds
    SafeJsonDouble<SafeJsonDoubleDefaultValue> maxFundingRate, // The upper limit of the predicted funding rate of the next cycle
    PooledString method, // Funding rate mechanism, current_period
    SafeJsonDouble<SafeJsonDoubleDefaultValue> minFundingRate, // The lower limit of the predicted funding rate of the next cycle
    SafeJsonDouble<SafeJsonDoubleDefaultValue> premium, // Premium between the mid price of perps market and the index price
    SafeJsonDouble<SafeJsonDoubleDefaultValue> settFundingRate, // If settState = processing, it is the funding rate that is being used for the current settlement cycle. If settState = settled, it is the funding rate that is being used for the previous settlement cycle
    PooledString settState, // Settlement state of funding rate, processing or settled
    JsonLong ts // Data return time, Unix timestamp format in milliseconds
)
{
    public DateTimeOffset Date => DateTimeOffset.FromUnixTimeMilliseconds(ts);
}