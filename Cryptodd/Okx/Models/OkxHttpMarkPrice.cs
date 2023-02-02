using System.Diagnostics.CodeAnalysis;
using Cryptodd.Json;

namespace Cryptodd.Okx.Models;

[SuppressMessage("ReSharper", "InconsistentNaming")]
// ReSharper disable once ClassNeverInstantiated.Global
public readonly record struct OkxHttpMarkPrice(
    PooledString instType,
    PooledString instId,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> markPx,
    JsonLong ts);