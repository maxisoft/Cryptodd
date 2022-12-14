using System.Collections;
using System.Runtime.CompilerServices;
using Cryptodd.IoC;
using Cryptodd.OrderBooks;
using Cryptodd.OrderBooks.Writer;
using Lamar;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Binance.Orderbooks.Handlers;

public struct
    BinanceFloatSerializableConverterConverter : IFloatSerializableConverter<DetailedOrderbookEntryFloatTuple,
        DetailedOrderbookEntryFloatTuple>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public DetailedOrderbookEntryFloatTuple Convert(in DetailedOrderbookEntryFloatTuple priceSizePair) => priceSizePair;
}

[Singleton]
public sealed class BinanceOrderBookWriter :
    OrderBookWriter<DetailedOrderbookEntryFloatTuple, DetailedOrderbookEntryFloatTuple,
        BinanceFloatSerializableConverterConverter,
        OrderBookWriterOptions>, IService
{
    internal const string ConfigurationSection = "Binance:OrderBook:File";

    public BinanceOrderBookWriter(ILogger logger, IConfiguration configuration, IServiceProvider serviceProvider) :
        base(logger, configuration.GetSection(ConfigurationSection), serviceProvider)
    {
        Options.CoalesceExchange("Binance");
    }
}

public class BinanceOrderbookSaveToFileHandler : IBinanceAggregatedOrderbookHandler, IService
{
    private readonly BinanceOrderBookWriter _writer;

    public BinanceOrderbookSaveToFileHandler(BinanceOrderBookWriter writer)
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