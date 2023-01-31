using System.Diagnostics.CodeAnalysis;
using Cryptodd.Json;

namespace Cryptodd.Okx.Models;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public record OkxWebsocketFundingRateResponse(OkxWebSocketArgWithChannelAndInstrumentId arg,
    OneItemList<OkxHttpFundingRate> data);