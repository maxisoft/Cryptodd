using System.Diagnostics.CodeAnalysis;
using Cryptodd.Json;
using Maxisoft.Utils.Collections.Lists.Specialized;

namespace Cryptodd.Okx.Models;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public readonly record struct TickerInfo(
    PooledString instType,
    PooledString instId,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> last,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> lastSz,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> askPx,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> askSz,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> bidPx,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> bidSz,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> open24h,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> high24h,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> low24h,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> volCcy24h,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> vol24h,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> sodUtc0,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> sodUtc8,
    JsonLong ts);

[SuppressMessage("ReSharper", "InconsistentNaming")]
public record class GetTikersResponse(JsonLong code, PooledString msg, PooledList<TickerInfo> data) { }