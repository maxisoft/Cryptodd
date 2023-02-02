using System.Diagnostics.CodeAnalysis;
using Cryptodd.Json;
using Maxisoft.Utils.Collections.Lists.Specialized;

namespace Cryptodd.Okx.Models.HttpResponse;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public record OkxHttpGetOpenInterestResponse
    (JsonLong code, PooledString msg, PooledList<OkxHttpOpenInterest> data) : BaseOxkHttpResponse(code, msg) { }