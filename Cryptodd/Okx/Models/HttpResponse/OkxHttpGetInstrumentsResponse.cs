using System.Diagnostics.CodeAnalysis;
using Cryptodd.Json;

namespace Cryptodd.Okx.Models.HttpResponse;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public record OkxHttpGetInstrumentsResponse
    (JsonLong code, PooledString msg, List<OkxHttpInstrumentInfo> data) : BaseOxkHttpResponse(code, msg) { }