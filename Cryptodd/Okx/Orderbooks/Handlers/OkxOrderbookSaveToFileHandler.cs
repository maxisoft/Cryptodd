using System.Collections;
using Cryptodd.Okx.Models;

namespace Cryptodd.Okx.Orderbooks.Handlers;

public class OkxOrderbookSaveToFileHandler : IOkxGroupedOrderbookHandler
{
    private readonly OkxOrderBookWriter _writer;

    public OkxOrderbookSaveToFileHandler(OkxOrderBookWriter writer)
    {
        _writer = writer;
    }

    private readonly struct ConcatBidAsk : IReadOnlyCollection<OkxOrderbookEntry>
    {
        private readonly OkxOrderbookEntry[] _asks;
        private readonly OkxOrderbookEntry[] _bids;

        public ConcatBidAsk(in OkxAggregatedOrderbookHandlerArguments arguments)
        {
            _asks = arguments.Asks;
            _bids = arguments.Bids;
        }

        public IEnumerator<OkxOrderbookEntry> GetEnumerator()
        {
            for (var index = 0; index < _bids.Length; index++)
            {
                var bid = _bids[index];
                yield return bid;
            }

            for (var index = 0; index < _asks.Length; index++)
            {
                var ask = _asks[index];
                yield return ask;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public int Count => _asks.Length + _bids.Length;
    }

    public async Task Handle(OkxAggregatedOrderbookHandlerArguments arguments, CancellationToken cancellationToken)
    {
        var dateTime = arguments.DateTime;
        if (dateTime <= DateTimeOffset.UnixEpoch)
        {
            throw new ArgumentOutOfRangeException(nameof(arguments), arguments,
                "Invalid negative DateTimeOffset for an orderbook");
        }

        await _writer.WriteAsync(arguments.Instrument, new ConcatBidAsk(in arguments), dateTime,
            cancellationToken).ConfigureAwait(false);
    }
}