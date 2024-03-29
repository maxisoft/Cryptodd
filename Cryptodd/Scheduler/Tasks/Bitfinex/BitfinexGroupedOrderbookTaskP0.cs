﻿using Cryptodd.Bitfinex;
using Lamar;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Scheduler.Tasks.Bitfinex;

public class BitfinexGroupedOrderbookTaskP0 : BitfinexGroupedOrderbookTask
{

    protected override BitfinexGatherGroupedOrderBookService GetOrderBookService() =>
        Container.GetInstance<BitfinexGatherGroupedOrderBookServiceP0>();

    public BitfinexGroupedOrderbookTaskP0(IContainer container, ILogger logger, IConfiguration configuration) : base(
        container, logger, configuration)
    {
        PeriodOffset -= TimeSpan.FromSeconds(5);
        Section = Configuration.GetSection("Bitfinex:OrderBookP0:Task");
        OnConfigurationChange();
    }
}