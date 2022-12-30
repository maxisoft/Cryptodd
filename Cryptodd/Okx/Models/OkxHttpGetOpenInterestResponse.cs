using System.Diagnostics.CodeAnalysis;
using Cryptodd.Json;
using Maxisoft.Utils.Collections.Lists.Specialized;

namespace Cryptodd.Okx.Models;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public readonly record struct OkxHttpOpenInterest(
    PooledString instType,
    PooledString instId,
    SafeJsonDouble<SafeJsonDoubleDefaultValue> oi,
    SafeJsonDouble<SafeJsonDoubleDefaultValue> oiCcy,
    JsonLong ts
);

[SuppressMessage("ReSharper", "InconsistentNaming")]
public record OkxHttpGetOpenInterestResponse
    (JsonLong code, PooledString msg, PooledList<OkxHttpOpenInterest> data) : BaseOxkHttpResponse(code, msg) { }