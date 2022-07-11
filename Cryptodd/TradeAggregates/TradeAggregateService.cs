using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Data;
using Cryptodd.Algorithms;
using Cryptodd.Databases.Postgres;
using Cryptodd.Ftx;
using Cryptodd.Ftx.Models;
using Cryptodd.IoC;
using Cryptodd.Pairs;
using Dapper;
using Lamar;
using MathNet.Numerics.Statistics;
using Maxisoft.Utils.Algorithms;
using Maxisoft.Utils.Empties;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using PetaPoco;
using PetaPoco.Extensions;
using PetaPoco.SqlKata;
using Polly;
using Serilog;
using SqlKata;
using SqlKata.Execution;
using TDigest;

namespace Cryptodd.TradeAggregates;

public class TradeAggregateOptions
{
    public int MaxParallelism { get; set; }

    public List<long> ResamplePeriods { get; set; } = new List<long>() {};
    public List<long> ResampleOffsets { get; set; } = new List<long>() {};

    public TradeAggregateOptions()
    {
        MaxParallelism = Environment.ProcessorCount.Clamp(1, 32);
    }
}

public interface ITradeAggregateService : IService
{
    Task Update(CancellationToken cancellationToken);
}

public class TradeAggregateService : ITradeAggregateService
{
    private readonly IConfiguration _configuration;
    private readonly IContainer _container;
    private readonly ILogger _logger;
    private readonly IPairFilterLoader _pairFilterLoader;
    private readonly ITradeDatabaseService _tradeDatabaseService;
    private readonly TradeAggregateOptions Options = new();

    /// <summary>
    ///     As soon as TradeCollector collect new item it should emit event
    /// </summary>
    private readonly ConcurrentDictionary<(string market, long time), EmptyStruct> pendingUpdates = // TODO implement it
        new();

    public TradeAggregateService(IContainer container, IConfiguration configuration, ILogger logger,
        IPairFilterLoader pairFilterLoader, ITradeDatabaseService tradeDatabaseService)
    {
        _container = container;
        _configuration = configuration;
        _logger = logger.ForContext(GetType());
        _pairFilterLoader = pairFilterLoader;
        _tradeDatabaseService = tradeDatabaseService;
        configuration.GetSection("Trade").GetSection("Aggregate").Bind(Options, options => options.ErrorOnUnknownConfiguration = true);
    }

    public async Task Update(CancellationToken cancellationToken)
    {
        var marketNames = await GetMarketNames(cancellationToken);

        using var semaphore = new SemaphoreSlim(Options.MaxParallelism, Options.MaxParallelism);
        await using var container = _container.GetNestedContainer();
        await using var connectionPool = container.GetRequiredService<IDynamicMiniConnectionPool>();
        connectionPool.ChangeCapSize(Options.MaxParallelism);
        while (!cancellationToken.IsCancellationRequested && semaphore.CurrentCount > 0)
        {
            var closeToCurrentTime = true;
            var minDelay = TimeSpan.MaxValue;
            await Parallel.ForEachAsync(marketNames, cancellationToken, async (marketName, token) =>
            {
                await Parallel.ForEachAsync(Options.ResamplePeriods.Zip(Options.ResampleOffsets), token, async (resamplePair, token) =>
                {
                    var resamplePeriod = resamplePair.First;
                    var resampleOffset = TimeSpan.FromMilliseconds(resamplePair.Second);
                    // ReSharper disable once AccessToDisposedClosure
                    await semaphore.WaitAsync(token);
                    try
                    {
                        var period = TimeSpan.FromMilliseconds(resamplePeriod);
                        var prevTime = long.MinValue;
                        await Policy.Handle<InvalidOperationException>(exception =>
                                (exception.Message?.Contains("NpgsqlTransaction") ?? false) &&
                                !token.IsCancellationRequested
                                )
                            .WaitAndRetry(1, i => TimeSpan.FromSeconds(i))
                            .Execute(async () =>
                            {
                                // ReSharper disable once AccessToDisposedClosure
                                prevTime = await GetSavedTime(connectionPool, marketName, period, resampleOffset, token).ConfigureAwait(false);
                            }).ConfigureAwait(false);
                        
                        if (prevTime == long.MinValue)
                        {
                            throw new Exception("prevTime not set");
                        }

                        // ReSharper disable once AccessToDisposedClosure
                        await CreateAggregates(connectionPool, marketName, period, resampleOffset, prevTime, token).ConfigureAwait(false);
                        closeToCurrentTime &=
                            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - prevTime
                                      < period.TotalMilliseconds * 2;
                        if (period < minDelay)
                        {
                            minDelay = period;
                        }
                    }
                    finally
                    {
                        // ReSharper disable once AccessToDisposedClosure
                        semaphore.Release();
                    }
                }).ConfigureAwait(true);
            }).ConfigureAwait(true);
            
            if (closeToCurrentTime && minDelay != TimeSpan.MaxValue)
            {
                minDelay /= 2;

                if (minDelay > TimeSpan.FromSeconds(5))
                {
                    minDelay = TimeSpan.FromSeconds(5);
                }
                await Task.Delay(minDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<ImmutableList<string>> GetMarketNames(CancellationToken cancellationToken)
    {
        ImmutableList<string> marketNames;


        await using var container = _container.GetNestedContainer();
        var http = container.GetInstance<IFtxPublicHttpApi>();
        var pairFilter = await _pairFilterLoader.GetPairFilterAsync("Trade", cancellationToken)
            .ConfigureAwait(false);
        using var markets = await http.GetAllMarketsAsync(cancellationToken).ConfigureAwait(false);

        marketNames = markets.Select(market => market.Name).Where(name => pairFilter.Match(name)).ToImmutableList();

        return marketNames;
    }

    private async Task CreateAggregates(IMiniConnectionPool connectionPool, string marketName, TimeSpan period, TimeSpan offset,
        long prevTime,
        CancellationToken cancellationToken)
    {
        using var container = _container.GetNestedContainer();
        using var db = container.GetInstance<QueryFactory>();
        await using var rent = await connectionPool.RentAsync(cancellationToken).ConfigureAwait(false);
        using var _ = new HijackQueryFactoryConnection(db, rent);
        var connection = rent.Connection;
        var dbConnect = Task.CompletedTask;
        if (connection.State != ConnectionState.Open)
        {
            dbConnect = connection.OpenAsync(cancellationToken);
        }

        var freshStart = prevTime == 0;
        var offsetMs = (long)offset.TotalMilliseconds;
        if (freshStart)
        {
            await dbConnect;
            await using var tmpTransaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            prevTime = await _tradeDatabaseService.GetFirstTime(tmpTransaction, marketName, cancellationToken).ConfigureAwait(false) - offsetMs;
            await tmpTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        var periodMs = (long)period.TotalMilliseconds;
        var roundedPrevTime = prevTime / periodMs * periodMs + offsetMs;
        var nextTime = (prevTime / periodMs + 1) * periodMs + offsetMs;
        await dbConnect;
        await using var tr = await connection.BeginTransactionAsync(IsolationLevel.Serializable ,cancellationToken).ConfigureAwait(false);
        var tradeTime = await _tradeDatabaseService.GetLastTime(tr, marketName, false, cancellationToken)
            .ConfigureAwait(false);
        if (tradeTime == 0)
        {
            _logger.Warning("The trade table for {Market} is empty !", marketName);
            return;
        }

        var tableName = TradeAggregateTableName(marketName, period, offset);

        if (tradeTime < nextTime) // TODO check with trades ids too to detect changes
        {
            _logger.Verbose("No need to refresh {Table}", tableName);
            return;
        }
        
        long tradeId;
        long prevnum_trades;
        do
        {
            tradeId = -1;
            prevnum_trades = -1;
            var data = await db.Query($"ftx.{tableName}")
                .SelectRaw("COALESCE(trade_id, -1)::bigint as trade_id")
                .SelectRaw("COALESCE(num_trades, -1)::bigint as num_trades")
                .Where("time", "=", roundedPrevTime)
                .Where("trade_id", "=", new Query($"ftx.{_tradeDatabaseService.TradeTableName(marketName)} as t")
                    .Select("t.id")
                    .Where("t.time", ">=", roundedPrevTime)
                    .Where("t.time", "<", nextTime)
                    .OrderByDesc("t.time", "t.id")
                    .Limit(1))
                .Limit(1).FirstOrDefaultAsync<dynamic>(tr, cancellationToken: cancellationToken);

            if (data is { })
            {
                tradeId = data.trade_id;
                prevnum_trades = data.num_trades;
            }


            if (tradeId > 0)
            {
                prevTime = nextTime;
                roundedPrevTime = prevTime / periodMs * periodMs + offsetMs;
                nextTime = roundedPrevTime + periodMs;
            }
        } while (tradeId > 0);
    
        await db.Query($"ftx.{tableName}")
            .Where("time", ">=", roundedPrevTime)
            .Where("time", "<", nextTime)
            .DeleteAsync(tr, cancellationToken: cancellationToken).ConfigureAwait(false);

        long id, time;
        float price, volume, open, high, low;
        FtxTradeFlag flag;
        price = high = low = open = 0; // TODO get previous candle close

        var priceStats = new RunningWeightedStatistics();
        var volumeStats = new RunningStatistics();
        var priceRegression = new RunningWeightedRegression();
        var priceLogRegression = new RunningWeightedRegression();
        var priceEma = ExponentialMovingAverage.FromSpan(prevnum_trades > 0 ? prevnum_trades : 23);
        var priceQuantiles = new MergingDigest(100);
        var volumeQuantiles = new MergingDigest(100);

        var counter = 0;
        var priceScale = 0.0;
        double volumeSum = 0;
        var buyCounter = 0;
        double buyVolume = 0;
        double liquidationVolumeSum = 0;
        var tId = 0L;
        long maxTime = 0;

        float closePrevPeriod0, closePrevPeriod1;
        var closePrevPeriod2 = closePrevPeriod1 = closePrevPeriod0 = 0;

        // ReSharper disable SuggestVarOrType_BuiltInTypes
        long prevPeriod0 = roundedPrevTime + (long)TradeAggregatesUtils.PrevPeriod(period).TotalMilliseconds;
        long prevPeriod1 = roundedPrevTime + (long)TradeAggregatesUtils.PrevPeriod(period, 1).TotalMilliseconds;
        long prevPeriod2 = roundedPrevTime + (long)TradeAggregatesUtils.PrevPeriod(period, 2).TotalMilliseconds;
        // ReSharper restore SuggestVarOrType_BuiltInTypes

        var regressionStartTime = roundedPrevTime;

        while (counter == 0)
        {
            await using (var reader = await connection.BeginBinaryExportAsync(
                             $"COPY (SELECT * FROM ftx.\"{_tradeDatabaseService.TradeTableName(marketName)}\" WHERE \"time\" >= {roundedPrevTime} AND \"time\" < {nextTime}) TO STDOUT (FORMAT BINARY)",
                             cancellationToken))
            {
                while (await reader.StartRowAsync(cancellationToken) != -1)
                {
                    id = reader.Read<long>(NpgsqlDbType.Bigint);
                    time = reader.Read<long>(NpgsqlDbType.Bigint);
                    price = reader.Read<float>(NpgsqlDbType.Real);
                    volume = reader.Read<float>(NpgsqlDbType.Real);
                    flag = (FtxTradeFlag)reader.Read<short>(NpgsqlDbType.Smallint);

                    if (price <= 0 || volume <= 0)
                    {
                        continue;
                    }

                    if (time <= prevPeriod0)
                    {
                        closePrevPeriod0 = price;
                    }

                    if (time <= prevPeriod1)
                    {
                        closePrevPeriod1 = price;
                    }

                    if (time <= prevPeriod2)
                    {
                        closePrevPeriod2 = price;
                    }

                    if (counter == 0)
                    {
                        maxTime = time;
                        tId = id;
                        open = high = low = price;
                        // highly biased due to only account 1st price
                        priceScale = Math.Exp(Math.Round(Math.Log(price)) - 10);
                        regressionStartTime = time;
                        priceEma.Value = price;
                    }
                    else if (time >= maxTime)
                    {
                        tId = time == maxTime ? Math.Max(tId, id) : id;
                        maxTime = time;
                    }

                    var weight = volume / priceScale;
                    priceQuantiles.Add(price, (int)((long)weight).Clamp(1, 1 << 16));
                    volumeQuantiles.Add(volume);
                    priceStats.Push(value: price, weight: volume);
                    volumeStats.Push(volume);
                    priceRegression.Push((time - regressionStartTime) / 1000.0, price - open, 1);
                    priceLogRegression.Push((time - regressionStartTime) / 1000.0, MathF.Log2(price), volume);
                    priceEma.Push(price);
                    counter += 1;
                    high = Math.Max(high, price);
                    low = Math.Min(low, price);
                    volumeSum += volume;

                    if (flag.HasFlag(FtxTradeFlag.Buy))
                    {
                        buyCounter += 1;
                        buyVolume += volume;
                    }

                    if (flag.HasFlag(FtxTradeFlag.Liquidation))
                    {
                        liquidationVolumeSum += volume;
                    }
                }

                if (counter > 0)
                {
                    break;
                }

                await reader.CancelAsync();
            }

            // else search for the next time as there is a hole
            prevTime = await db.Query($"ftx.{_tradeDatabaseService.TradeTableName(marketName)} as t")
                .SelectRaw("COALESCE(MIN(t.time), 0)")
                .Where("t.time", ">=", nextTime)
                .Limit(1)
                .FirstOrDefaultAsync<long>(tr, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (prevTime <= 0)
            {
                return;
            }

            roundedPrevTime = prevTime / periodMs * periodMs + offsetMs;
            nextTime = roundedPrevTime + periodMs;
        }


        static double NanToNum(double x, double replacement = 0)
        {
            return double.IsFinite(x) ? x : replacement;
        }

        if (price > 0 && counter > 0)
        {
            // TODO adapt https://en.wikipedia.org/wiki/Moving_average#Approximating_the_EMA_with_a_limited_number_of_terms
            // to remove loop
            for (var i = counter; i < prevnum_trades; i++)
            {
                priceEma.Push(price);
            }
        }

        // TODO repace this query as it's memory consuming
        await db.Query($"ftx.{tableName}").InsertAsync(new
        {
            time = roundedPrevTime,
            open,
            high,
            low,
            close = price,
            volume = volumeSum,

            mean_price = NanToNum(priceStats.Mean),
            std_price = NanToNum(priceStats.StandardDeviation),
            kurtosis_price = NanToNum(priceStats.Kurtosis),
            skewness_price = NanToNum(priceStats.Skewness),
            ema_price = NanToNum(priceEma.Value),

            mean_volume = NanToNum(volumeStats.Mean),
            std_volume = NanToNum(volumeStats.StandardDeviation),
            kurtosis_volume = NanToNum(volumeStats.Kurtosis),
            skewness_volume = NanToNum(volumeStats.Skewness),
            max_volume = NanToNum(volumeStats.Maximum),

            buy_ratio = (buyCounter / Math.Max(counter, 1.0f)).Clamp(0, 1),
            buy_volume_ratio = (volumeSum > 0 ? buyVolume / volumeSum : 0.5).Clamp(0, 1),
            liquidation_volume_ratio = ((float)(volumeSum > 0 ? liquidationVolumeSum / volumeSum : .5)).Clamp(0, 1),
            num_trades = counter,

            price_q10 = NanToNum(priceQuantiles.Quantile(0.10)),
            price_q25 = NanToNum(priceQuantiles.Quantile(0.25)),
            price_q50 = NanToNum(priceQuantiles.Quantile(0.5)),
            price_q75 = NanToNum(priceQuantiles.Quantile(0.75)),
            price_q90 = NanToNum(priceQuantiles.Quantile(0.9)),
            
            volume_q50 = NanToNum(volumeQuantiles.Quantile(0.5)),
            volume_q75 = NanToNum(volumeQuantiles.Quantile(0.75)),
            volume_q90 = NanToNum(volumeQuantiles.Quantile(0.9)),
            volume_q95 = NanToNum(volumeQuantiles.Quantile(0.95)),

            close_prev_period0 = closePrevPeriod0,
            close_prev_period1 = closePrevPeriod1,
            close_prev_period2 = closePrevPeriod2,

            price_regression_slope = NanToNum(priceRegression.Slope()),
            price_regression_intercept = NanToNum(priceRegression.Intercept()),
            price_regression_correlation = NanToNum(priceRegression.Correlation()).Clamp(-1, 1),

            price_log_regression_slope = MathF.Pow(2, (float)NanToNum(priceLogRegression.Slope())),
            price_log_regression_intercept = NanToNum(priceLogRegression.Intercept()),

            trade_id = tId
        }, tr, cancellationToken: cancellationToken).ConfigureAwait(false);

        await tr.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public string TradeAggregateTableName(string market, TimeSpan period, TimeSpan offset)
    {
        var templateReplacement = offset == TimeSpan.Zero ? $"_{market}_{(long)period.TotalSeconds}s" : $"_{market}_{(long) offset.TotalSeconds}_{(long)period.TotalSeconds}s";
        return "ftx_trade_agg_template".Replace("_template",
            _tradeDatabaseService.EscapeMarket(templateReplacement));
    }

    private async ValueTask<long> GetSavedTime(IMiniConnectionPool connectionPool, string market, TimeSpan period, TimeSpan offset, CancellationToken cancellationToken,
        bool allowTableCreation = true)
    {
        await using var container = _container.GetNestedContainer();
        using var db = container.GetInstance<QueryFactory>();
        
        await using var rent = await connectionPool.RentAsync(cancellationToken).ConfigureAwait(false);
        using var _ = new HijackQueryFactoryConnection(db, rent);
        db.Connection = rent.Connection;
        var conn = rent.Connection;
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var tr = await conn.BeginTransactionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        var tableName = TradeAggregateTableName(market, period, offset);
        long maxTime;
        try
        {
            maxTime = await db.Query($"ftx.{tableName}").SelectRaw("coalesce(MAX(\"time\"), 0)").Limit(1)
                .FirstOrDefaultAsync<long>(tr, cancellationToken: cancellationToken).ConfigureAwait(true);
        }
        catch (PostgresException e)
        {
            if (!allowTableCreation)
            {
                throw;
            }

            if (e.SqlState != TradeDatabaseService.TableDoesNotExistsSqlState)
            {
                throw;
            }

            if (conn.State != ConnectionState.Open)
            {
                await conn.OpenAsync(cancellationToken);
            }
            else
            {
                await tr.DisposeAsync();
            }

            await CreateTradeTable(tableName, cancellationToken).ConfigureAwait(false);

            maxTime = await GetSavedTime(connectionPool, market, period, offset, cancellationToken, false).ConfigureAwait(false);
        }
        
        return maxTime;
    }

    private async ValueTask CreateTradeTable(string tableName,
        CancellationToken cancellationToken)
    {
        var query = await TradeDatabaseService.GetFileContents("ftx_trade_agg.sql");
        query = query.Replace("ftx_trade_agg_template", tableName);
        
        await using var container = _container.GetNestedContainer();
        using var db = container.GetInstance<QueryFactory>();
        await using var conn = (NpgsqlConnection)db.Connection;
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var tr = await conn.BeginTransactionAsync(IsolationLevel.Serializable ,cancellationToken: cancellationToken).ConfigureAwait(false);

        await using var cmd = new NpgsqlCommand(query, conn, tr);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await tr.CommitAsync(cancellationToken);
        _logger.Information("Created aggregate table for {TableName}", tableName);
    }
}