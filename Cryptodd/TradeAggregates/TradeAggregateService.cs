using System.Collections.Concurrent;
using System.Collections.Immutable;
using Cryptodd.Algorithms;
using Cryptodd.Ftx;
using Cryptodd.Ftx.Models;
using Cryptodd.IoC;
using Cryptodd.Pairs;
using Lamar;
using MathNet.Numerics.Statistics;
using Maxisoft.Utils.Algorithms;
using Maxisoft.Utils.Empties;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;
using PetaPoco;
using PetaPoco.Extensions;
using PetaPoco.SqlKata;
using Serilog;
using SqlKata;
using TDigest;

namespace Cryptodd.TradeAggregates;

public class TradeAggregateOptions
{
    public int MaxParallelism { get; set; }

    public List<int> ResamplePeriods { get; set; } = new List<int>() { 5 * 60 * 1000 };

    public TradeAggregateOptions()
    {
        MaxParallelism = Environment.ProcessorCount.Clamp(1, 32);
    }
}


public class TradeAggregateService : IService
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
        configuration.Bind(Options);
    }

    public async Task Update(CancellationToken cancellationToken)
    {
        var http = _container.GetInstance<IFtxPublicHttpApi>();
        var pairFilter = await _pairFilterLoader.GetPairFilterAsync("Trade", cancellationToken)
            .ConfigureAwait(false);
        using var markets = await http.GetAllMarketsAsync(cancellationToken);

        var marketNames = markets.Select(market => market.Name).Where(name => pairFilter.Match(name)).ToImmutableList();

        var resamplePeriods = _configuration.GetSection("Trade").GetSection("Aggregate")
            .GetValue("Resample", new List<int> { 5 * 60 * 1000 });

        using var semaphore = new SemaphoreSlim(Options.MaxParallelism, Options.MaxParallelism);
        
        while (!cancellationToken.IsCancellationRequested && semaphore.CurrentCount > 0)
        {
            await Parallel.ForEachAsync(marketNames, cancellationToken, async (marketName, token) =>
            {
                await Parallel.ForEachAsync(resamplePeriods, token, async (resamplePeriod, token) =>
                {
                    // ReSharper disable once AccessToDisposedClosure
                    await semaphore.WaitAsync(token);
                    try
                    {
                        var period = TimeSpan.FromMilliseconds(resamplePeriod);
                        var prevTime = await GetSavedTime(marketName, period, token).ConfigureAwait(false);

                        await CreateAggregates(marketName, period, prevTime, token).ConfigureAwait(false);
                    }
                    finally
                    {
                        // ReSharper disable once AccessToDisposedClosure
                        semaphore.Release();
                    }
                }).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
    }

    public async Task CreateAggregates(string marketName, TimeSpan period, long prevTime,
        CancellationToken cancellationToken)
    {
        var freshStart = prevTime == 0;
        if (freshStart)
        {
            prevTime = await _tradeDatabaseService.GetFirstTime(marketName, cancellationToken);
        }

        var periodMs = (long)period.TotalMilliseconds;
        var roundedPrevTime = prevTime / periodMs * periodMs;
        var nextTime = (prevTime / periodMs + 1) * periodMs;
        var tradeTime = await _tradeDatabaseService.GetLastTime(marketName, false, cancellationToken)
            .ConfigureAwait(false);
        if (tradeTime == 0)
        {
            _logger.Warning("The trade table for {Market} is empty !", marketName);
            return;
        }

        var tableName = TradeAggregateTableName(marketName, period);

        if (tradeTime < nextTime) // TODO check with trades ids too to detect changes
        {
            _logger.Verbose("No need to refresh {Table}", tableName);
            return;
        }

        using var database = _container.GetInstance<IDatabase>();
        using var tr = database.GetTransaction();
        var connection = (NpgsqlConnection)database.Connection;
        Sql sql;
        long tradeId;
        long prevnum_trades;
        do
        {
            tradeId = -1;
            prevnum_trades = -1;
            sql = new Query($"ftx.{tableName}")
                .SelectRaw("COALESCE(trade_id, -1)::bigint as trade_id")
                .SelectRaw("COALESCE(num_trades, -1)::bigint as num_trades")
                .Where("time", "=", roundedPrevTime)
                .Where("trade_id", "=", new Query($"ftx.{_tradeDatabaseService.TradeTableName(marketName)} as t")
                    .Select("t.id")
                    .Where("t.time", ">=", roundedPrevTime)
                    .Where("t.time", "<", nextTime)
                    .OrderByDesc("t.time", "t.id")
                    .Limit(1))
                .Limit(1)
                .ToSql(CompilerType.Postgres);
            using var reader =await database.QueryAsync<dynamic>(cancellationToken, sql);
            if (await reader.ReadAsync())
            {
                tradeId = reader.Poco.trade_id;
                prevnum_trades = reader.Poco.num_trades;
            }

            

            if (tradeId > 0)
            {
                prevTime = nextTime;
                roundedPrevTime = prevTime / periodMs * periodMs;
                nextTime = (prevTime / periodMs + 1) * periodMs;
            }
        } while (tradeId > 0);

        sql = new Query($"ftx.{tableName}").AsDelete().Where("time", "=", roundedPrevTime).ToSql(CompilerType.Postgres);
        await database.ExecuteAsync(cancellationToken, sql);

        long id, time;
        float price, volume, open, high, low;
        FtxTradeFlag flag;
        price = high = low = open = 0; // TODO get previous candle close

        var priceStats = new RunningWeightedStatistics();
        var volumeStats = new RunningStatistics();
        var priceRegression = new RunningWeightedRegression();
        var priceLogRegression = new RunningWeightedRegression();
        var priceEma = ExponentialMovingAverage.FromSpan(prevnum_trades > 0 ? prevnum_trades: 23);
        MergingDigest digest;
        digest = new MergingDigest(100);

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
                        tId = Math.Max(tId, id);
                        maxTime = time;
                    }

                    var weight = volume / priceScale;
                    digest.Add(price, (int)((long)weight).Clamp(1, 1 << 16));
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
            sql = new Query($"ftx.{_tradeDatabaseService.TradeTableName(marketName)} as t")
                .SelectRaw("COALESCE(MIN(t.time), 0)")
                .Where("t.time", ">=", nextTime)
                .Limit(1)
                .ToSql(CompilerType.Postgres);
            prevTime = (await database.FirstOrDefaultAsync<long?>(cancellationToken, sql)).GetValueOrDefault();
            if (prevTime <= 0)
            {
                return;
            }

            roundedPrevTime = prevTime / periodMs * periodMs;
            nextTime = (prevTime / periodMs + 1) * periodMs;
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
        

        sql = new Query($"ftx.{tableName}").AsInsert(new
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

            price_q10 = NanToNum(digest.Quantile(0.10)),
            price_q25 = NanToNum(digest.Quantile(0.25)),
            price_q50 = NanToNum(digest.Quantile(0.5)),
            price_q75 = NanToNum(digest.Quantile(0.75)),
            price_q90 = NanToNum(digest.Quantile(0.9)),

            close_prev_period0 = closePrevPeriod0,
            close_prev_period1 = closePrevPeriod1,
            close_prev_period2 = closePrevPeriod2,

            price_regression_slope = NanToNum(priceRegression.Slope()),
            price_regression_intercept = NanToNum(priceRegression.Intercept()),
            price_regression_correlation = NanToNum(priceRegression.Correlation()).Clamp(-1, 1),

            price_log_regression_slope = MathF.Pow(2, (float)NanToNum(priceLogRegression.Slope())),
            price_log_regression_intercept = NanToNum(priceLogRegression.Intercept()),

            trade_id = tId
        }).ToSql(CompilerType.Postgres);

        await database.ExecuteAsync(cancellationToken, sql);
        tr.Complete();
    }

    public string TradeAggregateTableName(string market, TimeSpan period) =>
        "ftx_trade_agg_template".Replace("_template",
            _tradeDatabaseService.EscapeMarket($"_{market}_{(long)period.TotalSeconds}s"));

    private async ValueTask<long> GetSavedTime(string market, TimeSpan period, CancellationToken cancellationToken,
        bool allowTableCreation = true)
    {
        using var database = _container.GetInstance<IDatabase>();
        using var tr = database.GetTransaction();
        if (database.Connection is not NpgsqlConnection pgconn)
        {
            _logger.Error("Only postgres supported for now");
            return 0;
        }

        var tableName = TradeAggregateTableName(market, period);
        var query = new Query($"ftx.{tableName}").SelectRaw("coalesce(MAX(\"time\"), 0)").Limit(1)
            .ToSql(CompilerType.Postgres);
        long maxTime;
        try
        {
            maxTime = await database.SingleOrDefaultAsync<long>(cancellationToken, query).ConfigureAwait(false);
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

            await using (var newConn = pgconn.CloneWith(pgconn.ConnectionString))
            {
                tr.Complete();
                await newConn.OpenAsync(cancellationToken);
                try
                {
                    await CreateTradeTable(tableName, newConn, cancellationToken);
                }
                finally
                {
                    await newConn.CloseAsync();
                }
            }

            return await GetSavedTime(market, period, cancellationToken, false);
        }

        return maxTime;
    }

    private async ValueTask CreateTradeTable(string tableName, NpgsqlConnection pgconn,
        CancellationToken cancellationToken)
    {
        var query = await TradeDatabaseService.GetFileContents("ftx_trade_agg.sql");
        query = query.Replace("ftx_trade_agg_template", tableName);
        await using var cmd = new NpgsqlCommand(query, pgconn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}