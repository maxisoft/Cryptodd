using System.Diagnostics.CodeAnalysis;
using Cryptodd.Json;
using Cryptodd.Okx.Models.HttpResponse;

namespace Cryptodd.Okx.Models;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public record OkxHttpGetFundingRateResponse
    (JsonLong code, PooledString msg, OneItemList<OkxHttpFundingRate> data) : BaseOxkHttpResponse(code, msg) { }