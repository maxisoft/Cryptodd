using System.Collections;
using Cryptodd.OrderBooks;
using Cryptodd.OrderBooks.Writer;

namespace Cryptodd.Binance.Orderbooks.Handlers;

public abstract class BaseBinanceOrderbookSaveToFileHandler<TBinanceOrderBookWriter, TOptions>
    where TOptions: OrderBookWriterOptions, new()
    where TBinanceOrderBookWriter: OrderBookWriter<DetailedOrderbookEntryFloatTuple, DetailedOrderbookEntryFloatTuple,
        BinanceFloatSerializableConverterConverter,
        TOptions>
{
    private readonly TBinanceOrderBookWriter _writer;

    protected BaseBinanceOrderbookSaveToFileHandler(TBinanceOrderBookWriter writer)
    {
        _writer = writer;
    }

    private readonly struct ConcatBidAsk : IReadOnlyCollection<DetailedOrderbookEntryFloatTuple>
    {
        private readonly DetailedOrderbookEntryFloatTuple[] _asks;
        private readonly DetailedOrderbookEntryFloatTuple[] _bids;

        public ConcatBidAsk(in BinanceAggregatedOrderbookHandlerArguments arguments)
        {
            _asks = arguments.Asks;
            _bids = arguments.Bids;
        }

        public IEnumerator<DetailedOrderbookEntryFloatTuple> GetEnumerator()
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

    public async Task Handle(BinanceAggregatedOrderbookHandlerArguments arguments, CancellationToken cancellationToken)
    {
        await _writer.WriteAsync(arguments.Symbol, new ConcatBidAsk(in arguments), arguments.DateTime, cancellationToken);
    }
}