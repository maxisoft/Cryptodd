using System.Diagnostics.CodeAnalysis;
using Cryptodd.Json;

namespace Cryptodd.Okx.Models;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public readonly record struct OkxHttpOpenInterest(
    PooledString instType,
    PooledString instId,
    SafeJsonDouble<SafeJsonDoubleDefaultValue> oi,
    SafeJsonDouble<SafeJsonDoubleDefaultValue> oiCcy,
    JsonLong ts
);