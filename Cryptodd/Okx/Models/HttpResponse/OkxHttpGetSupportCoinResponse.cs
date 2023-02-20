using System.Diagnostics.CodeAnalysis;
using Cryptodd.Json;
using Cryptodd.Okx.Models.RubikStats;

namespace Cryptodd.Okx.Models.HttpResponse;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public record OkxHttpGetSupportCoinRatioResponse
    (JsonLong code, PooledString msg, OkxHttpSupportCoin data) : BaseOxkHttpResponse(code, msg) { }