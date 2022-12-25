using System.Collections;
using System.Runtime.CompilerServices;
using Cryptodd.Binance.Orderbooks.Handlers;
using Cryptodd.IoC;
using Cryptodd.Okx.Models;
using Cryptodd.OrderBooks;
using Cryptodd.OrderBooks.Writer;
using Lamar;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Okx.Orderbooks.Handlers;

public class OkxOrderBookWriterOptions : OrderBookWriterOptions { }

public struct
    OkxFloatSerializableConverterConverter : IFloatSerializableConverter<OkxOrderbookEntry,
        PriceSizeCountTriplet>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public PriceSizeCountTriplet Convert(in OkxOrderbookEntry priceSizePair) =>
        new PriceSizeCountTriplet((float)priceSizePair.Price, (float)priceSizePair.Quantity, priceSizePair.Count);
}

[Singleton]
public class OkxOrderBookWriter : OrderBookWriter<OkxOrderbookEntry, PriceSizeCountTriplet,
    OkxFloatSerializableConverterConverter,
    OkxOrderBookWriterOptions>
{
    public OkxOrderBookWriter(ILogger logger, IConfiguration configuration, IServiceProvider serviceProvider) : base(
        logger, configuration.GetSection("Okx:Orderbook:Writer"), serviceProvider)
    {
        Options.CoalesceExchange("Okx");
    }
}

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