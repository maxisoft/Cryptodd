using System.Collections.Immutable;
using System.Data;
using System.Net;
using System.Net.Sockets;
using Cryptodd.Databases.Postgres;
using Cryptodd.Ftx;
using Cryptodd.Ftx.Models;
using Cryptodd.Pairs;
using Dapper;
using Lamar;
using Maxisoft.Utils.Collections.Lists.Specialized;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using Polly;
using Serilog;
using SqlKata.Compilers;
using SqlKata.Execution;

namespace Cryptodd.TradeAggregates;

public class TradeCollector : ITradeCollector, IDisposable
{
    private const string LockErrorSqlState = "55P03";
    private readonly IConfiguration _configuration;
    private readonly IContainer _container;
    private readonly ILogger _logger;
    private readonly TradeCollectorOptions _options = new();
    private readonly IPairFilterLoader _pairFilterLoader;
    private readonly ITradePeriodOptimizer _periodOptimizer;
    private readonly IConfigurationSection _section;
    private readonly ITradeDatabaseService _tradeDatabaseService;

    public TradeCollector(IContainer container, ILogger logger, IPairFilterLoader pairFilterLoader,
        ITradeDatabaseService tradeDatabaseService, IConfiguration configuration, ITradePeriodOptimizer periodOptimizer)
    {
        _container = (IContainer)container.GetNestedContainer();
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
        if (!_options.Enabled)
        {
            return;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await using var container = _container.GetNestedContainer();
        cts.CancelAfter(_options.Timeout);
        var token = cts.Token;
        using var http = container.GetInstance<IFtxPublicHttpApi>();
        var pairFilter = await _pairFilterLoader.GetPairFilterAsync(_options.PairFilterName, cancellationToken)
            .ConfigureAwait(false);
        using var markets = await http.GetAllMarketsAsync(token);

        var marketNames = markets.Select(market => market.Name).Where(name => pairFilter.Match(name)).ToImmutableList();

        using var semaphore = new SemaphoreSlim(_options.MaxParallelism, _options.MaxParallelism);
        await using var connectionPool = container.GetRequiredService<IDynamicMiniConnectionPool>();
        connectionPool.ChangeCapSize(_options.MaxParallelism);
        var exceptions = new List<Exception>();
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Parallel.ForEachAsync(marketNames.OrderBy(_ => Guid.NewGuid()), token,
                    async (marketName, token) =>
                    {
                        // ReSharper disable once AccessToDisposedClosure
                        await semaphore.WaitAsync(token).ConfigureAwait(false);
                        try
                        {
                            // ReSharper disable once AccessToDisposedClosure
                            var savedTime = await GetSavedTime(connectionPool, marketName, token);

                            if (savedTime < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                            {
                                try
                                {
                                    // ReSharper disable twice AccessToDisposedClosure
                                    await Policy.Handle<HttpRequestException>(exception =>
                                            !token.IsCancellationRequested &&
                                            exception.StatusCode is HttpStatusCode.ServiceUnavailable
                                                or HttpStatusCode.InternalServerError or HttpStatusCode.BadRequest)
                                        .RetryAsync(2, (_, _) => _periodOptimizer.Reset(marketName)).WrapAsync(Policy
                                            .Handle<PostgresException>(exception =>
                                                exception.SqlState == LockErrorSqlState &&
                                                !token.IsCancellationRequested &&
                                                _options.LockTable)
                                            .WaitAndRetryAsync(5, i => TimeSpan.FromSeconds(0.5 + i)))
                                        .ExecuteAsync(() =>
                                            DownloadAndInsert(connectionPool, http, marketName, savedTime, token));
                                }
                                catch (Exception e) when
                                    (e is PostgresException or WebException or HttpRequestException)
                                {
                                    _periodOptimizer.Reset(marketName);
                                    lock (exceptions)
                                    {
                                        exceptions.Add(e);
                                    }
                                }
                            }
                        }
                        finally
                        {
                            // ReSharper disable once AccessToDisposedClosure
                            semaphore.Release();
                        }
                    }).ConfigureAwait(true);
            }
            catch (Exception e) when (e is OperationCanceledException or TaskCanceledException)
            {
                if (!cts.IsCancellationRequested)
                {
                    throw;
                }
            }
            catch (Exception e) when (e is PostgresException or WebException or HttpRequestException or SocketException)
            {
                _logger.Debug(e, "Got web exception => clearing the http client");
                http.DisposeHttpClient = true;
                throw;
            }

            if (exceptions.Any())
            {
                http.DisposeHttpClient = true;
                if (exceptions.Count == 1)
                {
                    throw exceptions.First();
                }

                throw new AggregateException(exceptions);
            }
        }
    }

    private async Task<long> GetSavedTime(IMiniConnectionPool connectionPool, string marketName,
        CancellationToken token)
    {
        await using var container = _container.GetNestedContainer();
        await using var rent = await connectionPool.RentAsync(token).ConfigureAwait(false);
        if (rent.Connection.State != ConnectionState.Open)
        {
            await rent.Connection.OpenAsync(token).ConfigureAwait(false);
        }

        await using var tr = await rent.Connection.BeginTransactionAsync(token).ConfigureAwait(false);
        var savedTime = await _tradeDatabaseService.GetLastTime(tr, marketName, cancellationToken: token)
            .ConfigureAwait(false);

        return savedTime;
    }

    private async Task DownloadAndInsert(IMiniConnectionPool connectionPool, IFtxPublicHttpApi api,
        string marketName, long prevTime,
        CancellationToken cancellationToken)
    {
        var wasZero = prevTime == 0;
        if (wasZero)
        {
            var ftxTradeRemoteStartTimeSearch = new FtxTradeRemoteStartTimeSearch(api, marketName);
            if (_options.MinimumDate > 0)
            {
                ftxTradeRemoteStartTimeSearch.MinimumDate =
                    DateTimeOffset.FromUnixTimeMilliseconds(_options.MinimumDate);
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
        using var trades = await GetTradesAsync(prevTime,
                Math.Min((long)(prevTime + apiPeriod.TotalMilliseconds),
                    (DateTimeOffset.Now + TimeSpan.FromMinutes(1)).ToUnixTimeMilliseconds()))
            .ConfigureAwait(false);

        if (!trades.Any())
        {
            return;
        }

        static void SortTrades(PooledList<FtxTrade> pooledList)
        {
            pooledList.AsSpan().Sort(static (left, right) =>
            {
                var cmp = left.Time.CompareTo(right.Time);
                return cmp == 0 ? left.Id.CompareTo(right.Id) : cmp;
            });
        }

        SortTrades(trades);

        await using var container = _container.GetNestedContainer();
        using var db = container.GetInstance<QueryFactory>();
        await using var rent = await connectionPool.RentAsync(cancellationToken).ConfigureAwait(false);
        using var _ = new HijackQueryFactoryConnection(db, rent);
        if (rent.Connection.State != ConnectionState.Open)
        {
            await rent.Connection.OpenAsync(cancellationToken);
        }

        await using var tr = await rent.Connection
            .BeginTransactionAsync(_options.LockTable ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted,
                cancellationToken).ConfigureAwait(false);
        var prevIds =
            await _tradeDatabaseService.GetLatestIds(tr, marketName, Math.Max(trades.Count + 1, 64), cancellationToken)
                .ConfigureAwait(false);

        prevIds.Sort();

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

        try
        {
            if (!await BulkInsert(tr, marketName, trades, prevTime, wasZero, prevIds, cancellationToken)
                    .ConfigureAwait(false))
            {
                return;
            }

            await tr.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (NpgsqlException)
        {
            _periodOptimizer.Reset(marketName);
            throw;
        }
    }

    private async Task<bool> BulkInsert(NpgsqlTransaction transaction, string marketName, PooledList<FtxTrade> trades,
        long startTime, bool freshStart,
        List<long> excludedIds, CancellationToken cancellationToken)
    {
        var tableName = _tradeDatabaseService.TradeTableName(marketName);
        if (_options.LockTable)
        {
            await transaction.Connection.ExecuteAsync(
                $"LOCK TABLE ftx.\"{tableName}\" IN SHARE ROW EXCLUSIVE MODE NOWAIT;", transaction: transaction);
        }


        var lastTime = await new XQuery(transaction.Connection, _container.GetInstance<Compiler>())
            .From($"ftx.{tableName}")
            .SelectRaw("COALESCE(MAX(\"time\"), 0)")
            .Limit(1)
            .FirstOrDefaultAsync<long>(transaction, cancellationToken: cancellationToken);
        if (lastTime != startTime && !(lastTime == 0 && freshStart))
        {
            _logger.Warning("Concurrent write on table {Table} detected", tableName);
            return false;
        }

        var maxId = excludedIds.LastOrDefault();
        var saveCount = 0;
        var connection = transaction.Connection!;
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
                    _logger.Warning("Duplicate trade skipped");
                    continue;
                }

                await writer.StartRowAsync(cancellationToken);
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
            await transaction.Connection!.ExecuteAsync($"SELECT setval('ftx.\"{tableName}_id_seq\"', {maxId}, true);",
                transaction: transaction);
        }

        _logger.Debug("Saved {Count} Trades for {MarketName} @ {Date:u}", saveCount, marketName,
            maxTime);
        return true;
    }

    public void Dispose()
    {
        _container.Dispose();
    }
}