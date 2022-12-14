using Cryptodd.Binance.Orderbooks.Handlers;
using Cryptodd.IoC;
using Cryptodd.OrderBooks;
using Cryptodd.OrderBooks.Writer;
using Lamar;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.BinanceFutures.Orderbooks.Handlers;

[Singleton]
// ReSharper disable once ClassNeverInstantiated.Global
public sealed class BinanceFuturesOrderBookWriter :
    OrderBookWriter<DetailedOrderbookEntryFloatTuple, DetailedOrderbookEntryFloatTuple,
        BinanceFloatSerializableConverterConverter,
        BinanceFuturesOrderBookWriterOptions>, IService
{
    public const string ConfigurationSection = "BinanceFutures:OrderBook:File";

    public BinanceFuturesOrderBookWriter(ILogger logger, IConfiguration configuration, IServiceProvider serviceProvider) :
        base(logger, configuration.GetSection(ConfigurationSection), serviceProvider)
    {
        Options.CoalesceExchange("BinanceFutures");
    }
}