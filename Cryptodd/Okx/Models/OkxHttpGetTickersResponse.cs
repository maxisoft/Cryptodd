using System.Diagnostics.CodeAnalysis;
using Cryptodd.Json;
using Maxisoft.Utils.Collections.Lists.Specialized;

namespace Cryptodd.Okx.Models;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public record OkxHttpGetTickersResponse(JsonLong code, PooledString msg, List<OkxHttpTickerInfo> data): BaseOxkHttpResponse(code, msg) { }