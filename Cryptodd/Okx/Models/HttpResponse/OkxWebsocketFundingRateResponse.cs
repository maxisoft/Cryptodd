using System.Diagnostics.CodeAnalysis;
using Cryptodd.Json;

namespace Cryptodd.Okx.Models.HttpResponse;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public record OkxWebsocketFundingRateResponse(OkxWebSocketArgWithChannelAndInstrumentId arg,
    OneItemList<OkxHttpFundingRate> data);