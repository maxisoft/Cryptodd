using System.Diagnostics.CodeAnalysis;
using Cryptodd.Json;
using Cryptodd.Okx.Models.RubikStats;

namespace Cryptodd.Okx.Models.HttpResponse;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public record OkxHttpGetContractsOpenInterestAndVolumeVolumeResponse
    (JsonLong code, PooledString msg, List<OkxHttpRubikOpenInterestVolume> data) : BaseOxkHttpResponse(code, msg) { }