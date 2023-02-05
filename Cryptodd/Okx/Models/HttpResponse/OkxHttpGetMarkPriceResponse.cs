using System.Diagnostics.CodeAnalysis;
using Cryptodd.Json;

namespace Cryptodd.Okx.Models.HttpResponse;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public record OkxHttpGetMarkPriceResponse
    (JsonLong code, PooledString msg, List<OkxHttpMarkPrice> data) : BaseOxkHttpResponse(code, msg) { }