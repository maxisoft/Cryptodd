using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Cryptodd.Ftx;
using Cryptodd.Ftx.Models;
using Cryptodd.Ftx.Models.DatabasePoco;
using Cryptodd.IoC;
using Cryptodd.Pairs;
using Lamar;
using Maxisoft.Utils.Collections.Lists.Specialized;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;
using PetaPoco;
using PetaPoco.SqlKata;
using Polly;
using Serilog;
using SqlKata;

namespace Cryptodd.TradeAggregates;

public class TradeCollector : ITradeCollector
{
    private const string LockErrorSqlState = "55P03";
    private readonly IContainer _container;
    private readonly ILogger _logger;
    private readonly IPairFilterLoader _pairFilterLoader;
    private readonly ITradePeriodOptimizer _periodOptimizer;
    private readonly ITradeDatabaseService _tradeDatabaseService;
    private readonly IConfiguration _configuration;
    private readonly IConfigurationSection _section;
    private readonly TradeCollectorOptions _options = new ();

    public TradeCollector(IContainer container, ILogger logger, IPairFilterLoader pairFilterLoader,
        ITradeDatabaseService tradeDatabaseService, IConfiguration configuration, ITradePeriodOptimizer periodOptimizer)
    {
        _container = container;
        _logger = logger.ForContext(GetType());
        _pairFilterLoader = pairFilterLoader;
        _tradeDatabaseService = tradeDatabaseService;
        _configuration = configuration;
        _periodOptimizer = periodOptimizer;
        _section = configuration.GetSection("Trade:Collector");
        _section.Bind(_options, options => options.ErrorOnUnknownConfiguration = true);
    }

    public async Task Collect(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_options.Timeout);
        var token = cts.Token;
        var http = _container.GetInstance<IFtxPublicHttpApi>();
        var pairFilter = await _pairFilterLoader.GetPairFilterAsync(_options.PairFilterName, cancellationToken)
            .ConfigureAwait(false);
        using var markets = await http.GetAllMarketsAsync(token);

        var marketNames = markets.Select(market => market.Name).Where(name => pairFilter.Match(name)).ToImmutableList();
        
        using var semaphore = new SemaphoreSlim(_options.MaxParallelism, _options.MaxParallelism);
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Parallel.ForEachAsync(marketNames, token, async (marketName, token) =>
                {
                    var savedTime = await _tradeDatabaseService.GetLastTime(marketName, cancellationToken: token).ConfigureAwait(false);
                    if (savedTime < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                    {
                        // ReSharper disable once AccessToDisposedClosure
                        await semaphore.WaitAsync(token).ConfigureAwait(false);
                        try
                        {
                            await Policy.Handle<PostgresException>(exception =>
                                    exception.SqlState == LockErrorSqlState && !token.IsCancellationRequested && _options.LockTable)
                                .WaitAndRetryAsync(10, i => TimeSpan.FromSeconds(0.5 + i))
                                .ExecuteAsync(() => DownloadAndInsert(http, marketName, savedTime, token))
                                .ConfigureAwait(false);
                        }
                        finally
                        {
                            // ReSharper disable once AccessToDisposedClosure
                            semaphore.Release();
                        }
                    }
                }).ConfigureAwait(true);
            }
            catch (Exception e) when (e is (OperationCanceledException or TaskCanceledException))
            {
                if (!cts.IsCancellationRequested)
                {
                    throw;
                }
            }
        }
    }

    private async Task DownloadAndInsert(IFtxPublicHttpApi api, string marketName, long prevTime,
        CancellationToken cancellationToken)
    {
        var wasZero = prevTime == 0;
        if (wasZero)
        {
            var ftxTradeRemoteStartTimeSearch = new FtxTradeRemoteStartTimeSearch(api, marketName);
            if (_options.MinimumDate > 0)
            {
                ftxTradeRemoteStartTimeSearch.MinimumDate = DateTimeOffset.FromUnixTimeMilliseconds(_options.MinimumDate);
            }
            
            prevTime = (await ftxTradeRemoteStartTimeSearch.Search(cancellationToken)
                    .ConfigureAwait(false))
                .ToUnixTimeMilliseconds();
            _logger.Information("Found start date {Date} for {Market}",
                DateTimeOffset.FromUnixTimeMilliseconds(prevTime), marketName);
        }

        Task<PooledList<FtxTrade>> GetTradesAsync(long startTime, long endTime)
        {
            return api.GetTradesAsync(marketName, startTime / 1000 - (wasZero ? 0 : 1),
                endTime / 1000,
                cancellationToken);
        }

        var apiPeriod = _periodOptimizer.GetPeriod(marketName);
        using var trades = await GetTradesAsync(prevTime, (long)(prevTime + apiPeriod.TotalMilliseconds))
            .ConfigureAwait(false);

        static void SortTrades(PooledList<FtxTrade> pooledList)
        {
            pooledList.AsSpan().Sort(static (left, right) =>
            {
                var cmp = left.Time.CompareTo(right.Time);
                return cmp == 0 ? left.Id.CompareTo(right.Id) : cmp;
            });
        }

        SortTrades(trades);

        using var db = _container.GetInstance<IDatabase>();
        using var tr = db.GetTransaction();
        var prevIds = await _tradeDatabaseService.GetLatestIds(marketName, trades.Count, db, cancellationToken);


        var mergeCounter = 0;
        var commonIdFound = wasZero;
        while (!commonIdFound && mergeCounter < 256)
        {
            for (var index = 0; index < trades.Count && !commonIdFound; index++)
            {
                var trade = trades[index];

                if (prevIds.BinarySearch(trade.Id) >= 0)
                {
                    commonIdFound = true;
                    break;
                }
            }

            if (commonIdFound)
            {
                break;
            }

            mergeCounter += 1;
            using var tmp = await GetTradesAsync(prevTime, trades.First().Time.ToUnixTimeMilliseconds())
                .ConfigureAwait(false);
            if (!tmp.Any())
            {
                break;
            }

            trades.InsertRange(0, tmp);
            SortTrades(trades);
        }

        _periodOptimizer.AdaptApiPeriod(marketName, mergeCounter, trades.Count);

        if (!await BulkInsert(marketName, trades, prevTime, db, wasZero, prevIds, cancellationToken))
        {
            return;
        }

        tr.Complete();
    }

    private async Task<bool> BulkInsert(string marketName, PooledList<FtxTrade> trades, long startTime,
        IDatabase database, bool freshStart,
        List<long> excludedIds, CancellationToken cancellationToken)
    {
        var tableName = _tradeDatabaseService.TradeTableName(marketName);
        if (_options.LockTable)
        {
            await database.ExecuteAsync(cancellationToken,
                $"LOCK TABLE ftx.\"{tableName}\" IN SHARE ROW EXCLUSIVE MODE NOWAIT;");
        }


        var query = new Query($"ftx.{tableName}").SelectRaw("COALESCE(MAX(\"time\"), 0)").Limit(1)
            .ToSql(CompilerType.Postgres);
        var lastTime = await database.FirstOrDefaultAsync<long>(cancellationToken, query);
        if (lastTime != startTime && !(lastTime == 0 && freshStart))
        {
            _logger.Warning("Concurrent write on table {Table} detected", tableName);
            return false;
        }

        var maxId = excludedIds.LastOrDefault();
        var saveCount = 0;
        var connection = (NpgsqlConnection)database.Connection;
        var maxTime = DateTimeOffset.FromUnixTimeMilliseconds(lastTime);
        await using (var writer =
                     await connection.BeginBinaryImportAsync(
                         $"COPY ftx.\"{tableName}\" FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            for (var index = 0; index < trades.Count; index++)
            {
                var trade = trades[index];
                if (excludedIds.BinarySearch(trade.Id) >= 0)
                {
                    continue;
                }

                if (index > 0 && trade.Id == trades[index - 1].Id)
                {
                    continue;
                }

                await writer.StartRowAsync(cancellationToken).ConfigureAwait(false);
#pragma warning disable CA2016
                // ReSharper disable MethodSupportsCancellation RedundantCast
                maxId = Math.Max(maxId, trade.Id);
                await writer.WriteAsync((long)trade.Id, NpgsqlDbType.Bigint);
                await writer.WriteAsync((long)trade.Time.ToUnixTimeMilliseconds(), NpgsqlDbType.Bigint);
                await writer.WriteAsync((float)trade.Price, NpgsqlDbType.Real);
                await writer.WriteAsync((float)trade.Size, NpgsqlDbType.Real);
                await writer.WriteAsync((short)trade.Flag, NpgsqlDbType.Smallint);
                // ReSharper restore MethodSupportsCancellation RedundantCast
#pragma warning restore CA2016
                saveCount += 1;
                maxTime = TradeAggregatesUtils.Max(maxTime, trade.Time);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        if (maxId > 0)
        {
            await database.ExecuteAsync(cancellationToken, $"SELECT setval('ftx.\"{tableName}_id_seq\"', @0, true);",
                maxId);
        }

        _logger.Debug("Saved {Count} Trades for {MarketName} @ {Date:u}", saveCount, marketName,
            maxTime);
        return true;
    }
}