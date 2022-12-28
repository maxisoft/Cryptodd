using System.Diagnostics.CodeAnalysis;
using Cryptodd.Binance.Orderbooks;
using Cryptodd.Bitfinex;
using Lamar;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Scheduler.Tasks.Binance;

// ReSharper disable once UnusedType.Global
public sealed class BinanceOrderbookTask : BaseBinanceOrderbookTask
{
    public BinanceOrderbookTask(IContainer container, ILogger logger, IConfiguration configuration) : base(container,
        logger, configuration)
    {
        Section = Configuration.GetSection("Binance:OrderBook:Task");
        OnConfigurationChange();
    }

    [SuppressMessage("ReSharper", "InvertIf")]
    protected override Task<BinanceOrderbookCollectorUnion> GetOrderBookService()
    {
        if (Collector.IsNull)
        {
            lock (LockObject)
            {
                if (Collector.IsNull)
                {
                    Collector = new BinanceOrderbookCollectorUnion(Container.GetInstance<BinanceOrderbookCollector>());
                }
            }
        }

        return Task.FromResult(Collector);
    }
}