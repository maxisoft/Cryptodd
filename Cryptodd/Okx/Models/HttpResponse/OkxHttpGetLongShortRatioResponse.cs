using System.Diagnostics.CodeAnalysis;
using Cryptodd.Json;
using Cryptodd.Okx.Models.RubikStats;

namespace Cryptodd.Okx.Models.HttpResponse;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public record OkxHttpGetLongShortRatioResponse
    (JsonLong code, PooledString msg, List<OkxHttpRubikLongShortRatio> data) : BaseOxkHttpResponse(code, msg) { }