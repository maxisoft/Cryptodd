using Cryptodd.Okx.Models;
using Cryptodd.OrderBooks;
using Cryptodd.OrderBooks.Writer;
using Lamar;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Okx.Orderbooks.Handlers;

[Singleton]
// ReSharper disable once ClassNeverInstantiated.Global
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