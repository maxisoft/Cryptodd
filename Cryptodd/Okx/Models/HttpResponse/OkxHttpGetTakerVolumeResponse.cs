using System.Diagnostics.CodeAnalysis;
using Cryptodd.Json;
using Cryptodd.Okx.Models.RubikStats;

namespace Cryptodd.Okx.Models.HttpResponse;

// ReSharper disable once ClassNeverInstantiated.Global
[SuppressMessage("ReSharper", "InconsistentNaming")]
public record OkxHttpGetTakerVolumeResponse
    (JsonLong code, PooledString msg, List<OkxHttpRubikTakerVolume> data) : BaseOxkHttpResponse(code, msg) { }

// ReSharper disable once ClassNeverInstantiated.Global

// ReSharper disable once ClassNeverInstantiated.Global

// ReSharper disable once ClassNeverInstantiated.Global