using System.Diagnostics.CodeAnalysis;
using Cryptodd.Json;
using Maxisoft.Utils.Collections.Lists.Specialized;

namespace Cryptodd.Okx.Models;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public record OkxHttpGetTikersResponse(JsonLong code, PooledString msg, PooledList<OkxHttpTickerInfo> data) { }