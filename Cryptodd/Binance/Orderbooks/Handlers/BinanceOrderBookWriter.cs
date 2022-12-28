using Cryptodd.IoC;
using Cryptodd.OrderBooks;
using Cryptodd.OrderBooks.Writer;
using Lamar;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Binance.Orderbooks.Handlers;

[Singleton]
// ReSharper disable once ClassNeverInstantiated.Global
public sealed class BinanceOrderBookWriter :
    OrderBookWriter<DetailedOrderbookEntryFloatTuple, DetailedOrderbookEntryFloatTuple,
        BinanceFloatSerializableConverterConverter,
        BinanceOrderBookWriterOptions>, IService
{
    public const string ConfigurationSection = "Binance:OrderBook:File";

    public BinanceOrderBookWriter(ILogger logger, IConfiguration configuration, IServiceProvider serviceProvider) :
        base(logger, configuration.GetSection(ConfigurationSection), serviceProvider)
    {
        Options.CoalesceExchange("Binance");
    }
}