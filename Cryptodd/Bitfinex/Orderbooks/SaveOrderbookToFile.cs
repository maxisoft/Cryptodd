using System.Data;
using System.Runtime.CompilerServices;
using Cryptodd.Bitfinex.Models;
using Cryptodd.Databases;
using Cryptodd.Databases.Postgres;
using Cryptodd.Databases.Tables.Bitfinex;
using Cryptodd.Features;
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
public sealed class BitfinexOrderBookWriter :
    OrderBookWriter<PriceCountSizeTuple, PriceSizeCountTriplet, BitfinexFloatSerializableConverterConverter, OrderBookWriterOptions>, IService
{
    internal const string ConfigurationSection = "Bitfinex:OrderBook:File";

    public BitfinexOrderBookWriter(ILogger logger, IConfiguration configuration, IServiceProvider serviceProvider) :
        base(logger, configuration.GetSection(ConfigurationSection), serviceProvider)
    {
        Options.CoalesceExchange("Bitfinex");
    }
}

public class SaveOrderbookToFile : IOrderbookHandler
{
    private readonly IContainer _container;
    private readonly Logger _logger;
    private readonly IConfiguration _configuration;
    private readonly BitfinexOrderBookWriter _writer;

    public SaveOrderbookToFile(Logger logger, IContainer container, BitfinexOrderBookWriter writer)
    {
        _logger = logger;
        _container = container;
        _configuration = _container.GetInstance<IConfiguration>()
            .GetSection(BitfinexOrderBookWriter.ConfigurationSection);
        _writer = writer;
    }

    public bool Disabled { get; set; }

    public async Task Handle(IReadOnlyCollection<OrderbookEnvelope> orderbooks, CancellationToken cancellationToken)
    {
        if (Disabled || !_writer.Options.Enabled)
        {
            return;
        }

        var maxParallelism = _writer.Options.MaxParallelism;
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
                await _writer.WriteAsync(envelope.Symbol, envelope.Orderbook,
                    DateTimeOffset.FromUnixTimeMilliseconds(envelope.Time), token);
            }
            finally
            {
                semaphore.Release();
            }
        });
    }
}