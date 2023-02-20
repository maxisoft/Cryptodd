using System.Data;
using System.Runtime.CompilerServices;
using Cryptodd.Bitfinex.Models;
using Cryptodd.Databases;
using Cryptodd.Databases.Postgres;
using Cryptodd.Databases.Tables.Bitfinex;
using Cryptodd.Features;
using Cryptodd.IO;
using Cryptodd.IoC;
using Cryptodd.OrderBooks;
using Cryptodd.OrderBooks.Writer;
using Lamar;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using PetaPoco.SqlKata;
using Serilog;
using Serilog.Core;

namespace Cryptodd.Bitfinex.Orderbooks;

public struct
    BitfinexFloatSerializableConverterConverter : IFloatSerializableConverter<PriceCountSizeTuple,
        PriceSizeCountTriplet>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public PriceSizeCountTriplet Convert(in PriceCountSizeTuple priceSizePair) =>
        new((float)priceSizePair.Price, (float)priceSizePair.Size,
            priceSizePair.Count);
}

[Singleton]
public sealed class BitfinexOrderBookWriterP2 :
    OrderBookWriter<PriceCountSizeTuple, PriceSizeCountTriplet, BitfinexFloatSerializableConverterConverter, OrderBookWriterOptions>, IService
{
    internal const string ConfigurationSection = "Bitfinex:OrderBook:File";

    public BitfinexOrderBookWriterP2(ILogger logger, IConfiguration configuration, IServiceProvider serviceProvider) :
        base(logger, configuration.GetSection(ConfigurationSection), serviceProvider)
    {
        Options.CoalesceExchange("Bitfinex");
    }
}

[Singleton]
public sealed class BitfinexOrderBookWriterP0 :
    OrderBookWriter<PriceCountSizeTuple, PriceSizeCountTriplet, BitfinexFloatSerializableConverterConverter, OrderBookWriterOptions>, IService
{
    internal const string ConfigurationSection = "Bitfinex:OrderBookP0:File";

    public BitfinexOrderBookWriterP0(ILogger logger, IConfiguration configuration, IServiceProvider serviceProvider) :
        base(logger, configuration.GetSection(ConfigurationSection), serviceProvider)
    {
        Options.CoalesceExchange("BitfinexP0");
    }
}

public class SaveOrderbookToFile : IOrderbookHandler
{
    private readonly IContainer _container;
    private readonly Logger _logger;
    private readonly IConfiguration _configuration;
    private readonly BitfinexOrderBookWriterP2 _writerP2;
    private readonly BitfinexOrderBookWriterP0 _writerP0;

    public SaveOrderbookToFile(Logger logger, IContainer container, BitfinexOrderBookWriterP2 writerP2, BitfinexOrderBookWriterP0 writerP0)
    {
        _logger = logger;
        _container = container;
        _writerP2 = writerP2;
        _writerP0 = writerP0;
        _configuration = _container.GetInstance<IConfiguration>()
            .GetSection(BitfinexOrderBookWriterP2.ConfigurationSection);
    }

    public bool Disabled { get; set; }

    private async Task DoHandle<T>(T writer, OrderbookHandlerQuery query, IReadOnlyCollection<OrderbookEnvelope> orderbooks,
        CancellationToken cancellationToken) where T: OrderBookWriter<PriceCountSizeTuple, PriceSizeCountTriplet, BitfinexFloatSerializableConverterConverter, OrderBookWriterOptions>
    {
        if (Disabled || !writer.Options.Enabled)
        {
            return;
        }

        var maxParallelism = writer.Options.MaxParallelism;
        if (maxParallelism <= 0)
        {
            maxParallelism += Environment.ProcessorCount;
        }

        using var semaphore = new SemaphoreSlim(maxParallelism, maxParallelism);

        await Parallel.ForEachAsync(orderbooks, cancellationToken, async (envelope, token) =>
        {
            if (cancellationToken.IsCancellationRequested || token.IsCancellationRequested)
            {
                return;
            }

            await semaphore.WaitAsync(token).ConfigureAwait(false);

            try
            {
                await writer.WriteAsync(envelope.Symbol, envelope.Orderbook,
                    DateTimeOffset.FromUnixTimeMilliseconds(envelope.Time), token);
            }
            finally
            {
                semaphore.Release();
            }
        });
    }

    public async Task Handle(OrderbookHandlerQuery query, IReadOnlyCollection<OrderbookEnvelope> orderbooks, CancellationToken cancellationToken)
    {
        switch (query.Precision)
        {
            case 2:
                await DoHandle(_writerP2, query, orderbooks, cancellationToken);
                break;
            case 0:
                await DoHandle(_writerP0, query, orderbooks, cancellationToken);
                break;
            default:
                throw new ArgumentException("", nameof(query));
        }
    }
}