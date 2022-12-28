using Cryptodd.Binance.Orderbooks;
using Cryptodd.Okx.Models;
using Cryptodd.OrderBooks;

namespace Cryptodd.Okx.Orderbooks.Handlers;

public record OkxAggregatedOrderbookHandlerArguments(
    OkxOrderbookEntry[] Asks,
    OkxOrderbookEntry[] Bids,
    OkxWebsocketOrderbookResponse OriginalResponse)
{
    public string Instrument => OriginalResponse.arg.instId;
    public DateTimeOffset DateTime => OriginalResponse.FirstData.Timestamp;
}
