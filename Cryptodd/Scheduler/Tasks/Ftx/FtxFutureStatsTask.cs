using System.Collections.Immutable;
using System.Diagnostics;
using Cryptodd.Ftx;
using Cryptodd.Ftx.Futures;
using Cryptodd.Ftx.Models;
using Cryptodd.Ftx.Models.DatabasePoco;
using Cryptodd.Pairs;
using Lamar;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Scheduler.Tasks.Ftx;

public class FtxFutureStatsTask : BasePeriodicScheduledTask
{
    private readonly IPairFilterLoader _pairFilterLoader;

    public FtxFutureStatsTask(ILogger logger, IConfiguration configuration, IContainer container,
        IPairFilterLoader pairFilterLoader) : base(logger,
        configuration, container)
    {
        Period = TimeSpan.FromMinutes(1);
        _pairFilterLoader = pairFilterLoader;
        OnConfigurationChange();
    }

    public override IConfigurationSection Section =>
        Configuration.GetSection("Ftx").GetSection("FutureStats").GetSection("Task");


    public override async Task Execute(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        await using var container = Container.GetNestedContainer();
        using var http = container.GetInstance<IFtxPublicHttpApi>();
        using var futures = await http.GetAllFuturesAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var pairFilter = await _pairFilterLoader.GetPairFilterAsync("Ftx.FutureStats", cancellationToken)
            .ConfigureAwait(false);

        var additionalStats = new Dictionary<string, ApiFutureStats>();
        await Parallel.ForEachAsync(futures.Select(stats => stats.Name), cancellationToken, async (market, token) =>
        {
            ApiFutureStats? stats = null;
            try
            {
                // ReSharper disable once AccessToDisposedClosure
                stats = await http.GetFuturesStatsAsync(market, token).ConfigureAwait(false);
            }
            catch (HttpRequestException e)
            {
                Logger.Error(e, "Getting futures stats for {Market}", market);
            }

            if (stats is null)
            {
                Logger.Warning("FuturesStats for {Market} is null", market);
                return;
            }

            lock (additionalStats)
            {
                additionalStats[market] = stats;
            }
        }).ConfigureAwait(false);

        FutureStats FutureToFutureStats(Future future)
        {
            var spread = 0f;
            if (future.Bid.GetValueOrDefault() > 0)
            {
                spread = (float)(future.Ask.GetValueOrDefault() / future.Bid.GetValueOrDefault() - 1);
            }

            var nextFundingRate = 0.0f;
            if (additionalStats.TryGetValue(future.Name, out var moreStats))
            {
                nextFundingRate = moreStats.NextFundingRate;
            }

            return new FutureStats
            {
                Spread = spread,
                Time = now,
                MarketHash = PairHasher.Hash(future.Name),
                OpenInterest = future.OpenInterest.GetValueOrDefault(),
                OpenInterestUsd = future.OpenInterestUsd.GetValueOrDefault(),
                Mark = (float)(future.Mark ?? future.Ask ?? future.Bid).GetValueOrDefault(),
                NextFundingRate = nextFundingRate
            };
        }

        var futureStats = futures
            .Where(future => pairFilter.Match(future.Name))
            .Select(FutureToFutureStats).ToImmutableArray();


        var handlers = container.GetAllInstances<IFuturesStatsHandler>();

        if (futureStats.Any() && handlers.Any())
        {
            await Parallel.ForEachAsync(handlers, cancellationToken, async (handler, token) =>
            {
                if (handler.Disabled)
                {
                    return;
                }

                try
                {
                    await handler.Handle(futureStats, token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Logger.Error(e, "{Type} failed to handle #{Count}", handler.GetType(), futureStats.Length);
                }
            });
        }


        Logger.Debug("Collected {Count} FutureStats in {Elapsed}", futureStats.Length, sw.Elapsed);
    }
}