using System.Diagnostics.CodeAnalysis;
using Cryptodd.Json;

namespace Cryptodd.Okx.Models;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public record OkxHttpSupportCoin(List<PooledString> contract, List<PooledString> option, List<PooledString> spot);