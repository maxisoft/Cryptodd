using System.Diagnostics.CodeAnalysis;
using Cryptodd.Json;

namespace Cryptodd.Okx.Models;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public record OkxOptionSummary(
    PooledString instType,
    PooledString instId,
    PooledString uly,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> delta,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> gamma,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> vega,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> theta,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> deltaBS,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> gammaBS,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> vegaBS,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> thetaBS,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> lever,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> markVol,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> bidVol,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> askVol,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> realVol,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> fwdPx,
    JsonLong ts
);