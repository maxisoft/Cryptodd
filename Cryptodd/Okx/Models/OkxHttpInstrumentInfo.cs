using System.Diagnostics.CodeAnalysis;
using Cryptodd.Json;

namespace Cryptodd.Okx.Models;

[SuppressMessage("ReSharper", "InconsistentNaming")]
[SuppressMessage("ReSharper", "IdentifierTypo")]
public record OkxHttpInstrumentInfo(
    PooledString instType,
    PooledString instId,
    PooledString instFamily,
    PooledString uly,
    PooledString category,
    PooledString baseCcy,
    PooledString quoteCcy,
    PooledString settleCcy,
    SafeJsonDouble<SafeJsonDoubleDefaultValueOne> ctVal,
    SafeJsonDouble<SafeJsonDoubleDefaultValueOne> ctMult,
    PooledString ctValCcy,
    PooledString optType,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> stk,
    JsonLong listTime,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> expTime,
    SafeJsonDouble<SafeJsonDoubleDefaultValueOne> lever,
    SafeJsonDouble<SafeJsonDoubleDefaultValueOne> tickSz,
    SafeJsonDouble<SafeJsonDoubleDefaultValueOne> lotSz,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> minSz,
    PooledString ctType,
    PooledString alias,
    PooledString state,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> maxLmtSz,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> maxMktSz,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> maxTwapSz,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> maxIcebergSz,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> maxTriggerSz,
    SafeJsonDouble<SafeJsonDoubleDefaultValueNegativeZero> maxStopSz
);