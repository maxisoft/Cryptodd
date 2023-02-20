using System.Diagnostics.CodeAnalysis;
using Cryptodd.Json;
using Cryptodd.Okx.Models.RubikStats;

namespace Cryptodd.Okx.Models.HttpResponse;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public record OkxHttpGetMarginLendingRatioResponse
    (JsonLong code, PooledString msg, List<OkxHttpRubikMarginLendingRatio> data) : BaseOxkHttpResponse(code, msg) { }