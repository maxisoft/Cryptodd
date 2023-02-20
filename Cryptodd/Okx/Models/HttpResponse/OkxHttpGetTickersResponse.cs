using System.Diagnostics.CodeAnalysis;
using Cryptodd.Json;

namespace Cryptodd.Okx.Models.HttpResponse;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public record OkxHttpGetTickersResponse
    (JsonLong code, PooledString msg, List<OkxHttpTickerInfo> data) : BaseOxkHttpResponse(code, msg) { }