using System.Diagnostics.CodeAnalysis;
using Cryptodd.Json;

namespace Cryptodd.Okx.Models;

[SuppressMessage("ReSharper", "InconsistentNaming")]
// ReSharper disable once ClassNeverInstantiated.Global
public record OkxHttpTickerInfo(
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