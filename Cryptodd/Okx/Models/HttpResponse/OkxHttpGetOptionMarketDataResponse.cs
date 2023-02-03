using System.Diagnostics.CodeAnalysis;
using Cryptodd.Json;

namespace Cryptodd.Okx.Models.HttpResponse;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public record OkxHttpGetOptionMarketDataResponse(JsonLong code, PooledString msg, List<OkxHttpOptionSummary> data): BaseOxkHttpResponse(code, msg) { }