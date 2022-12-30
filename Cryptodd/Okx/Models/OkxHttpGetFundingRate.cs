using System.Diagnostics.CodeAnalysis;
using Cryptodd.Json;

namespace Cryptodd.Okx.Models;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public record OkxHttpGetFundingRateResponse
    (JsonLong code, PooledString msg, OneItemList<OkxHttpFundingRate> data) : BaseOxkHttpResponse(code, msg) { }