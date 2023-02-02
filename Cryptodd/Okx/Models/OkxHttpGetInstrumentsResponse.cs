using System.Diagnostics.CodeAnalysis;
using Cryptodd.Json;
using Maxisoft.Utils.Collections.Lists.Specialized;

namespace Cryptodd.Okx.Models;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public record OkxHttpGetInstrumentsResponse
    (JsonLong code, PooledString msg, List<OkxHttpInstrumentInfo> data) : BaseOxkHttpResponse(code, msg) { }