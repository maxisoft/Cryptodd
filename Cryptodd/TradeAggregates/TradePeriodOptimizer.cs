using System.Collections.Concurrent;
using Cryptodd.Ftx;
using Cryptodd.IoC;
using Lamar;
using Microsoft.Extensions.Configuration;

namespace Cryptodd.TradeAggregates;

public interface ITradePeriodOptimizer : IService
{
    TradePeriodOptimizerOptions Options { get; }
    TimeSpan GetPeriod(string market);
    void AdaptApiPeriod(string market, int excessCounter, int numElements);
}

public class TradePeriodOptimizer : ITradePeriodOptimizer
{
    public TradePeriodOptimizerOptions
        Options { get; protected internal set; } = new();

    internal ConcurrentDictionary<string, TimeSpan> ApiPeriods { get; set; } = new();

    public TradePeriodOptimizer(IConfiguration configuration)
    {
        configuration.GetSection("Trade:PeriodOptimizer")
            .Bind(Options, options => options.ErrorOnUnknownConfiguration = true);
    }

    public TimeSpan GetPeriod(string market) => ApiPeriods.GetValueOrDefault(market, Options.DefaultApiPeriod);

    public void AdaptApiPeriod(string market, int excessCounter, int numElements)
    {
        TimeSpan apiPeriod;
        if (excessCounter > 0 || numElements >= FtxPublicHttpApi.TradeDefaultCapacity / Options.CapacityDivisor)
        {
            apiPeriod = ApiPeriods.GetOrAdd(market, Options.MinApiPeriod);
            apiPeriod = TradeAggregatesUtils.Clamp(apiPeriod * Math.Pow(1 / Options.Multiplier, Math.Max(excessCounter + 1, 1)),
                Options.MinApiPeriod, Options.MaxApiPeriod);
        }
        else
        {
            apiPeriod = ApiPeriods.GetOrAdd(market, Options.MinApiPeriod);
            apiPeriod = TradeAggregatesUtils.Clamp(apiPeriod * Options.Multiplier, Options.MinApiPeriod, Options.MaxApiPeriod);
        }

        ApiPeriods[market] = apiPeriod;
    }
}