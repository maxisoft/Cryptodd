using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cryptodd.Binance.Http;
using Cryptodd.Binance.Http.RateLimiter;
using Cryptodd.Binance.Models;
using Cryptodd.BinanceFutures.Http;
using Cryptodd.BinanceFutures.Http.RateLimiter;
using Cryptodd.IO;
using Cryptodd.IO.FileSystem;
using Cryptodd.IO.Mmap.Writer;
using Cryptodd.Pairs;
using Lamar;
using Maxisoft.Utils.Algorithms;
using Maxisoft.Utils.Collections.Lists.Specialized;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Binance.Collector.Klines;

public struct BinanceHttpKlineDoubleSerializerConverter : IDoubleSerializerConverter<BinanceHttpKline, BinanceHttpKline>
{
    public BinanceHttpKline Convert(in BinanceHttpKline doubleSerializable) => doubleSerializable;
}

public class BinanceHttpKlineDataWriterOptions : DataWriterOptions
{
    public const string DefaultExchange = "Binance";
    public const string DefaultKind = "kline";

    public BinanceHttpKlineDataWriterOptions()
    {
        CoalesceExchange(DefaultExchange);
        Kind = DefaultKind;

        MaxFileSize = 1L << 31;
        BufferCount = 64;
    }
}

public class BinanceFuturesHttpKlineDataWriterOptions : DataWriterOptions
{
    public const string DefaultExchange = "BinanceFutures";
    public const string DefaultKind = "kline";

    public BinanceFuturesHttpKlineDataWriterOptions()
    {
        CoalesceExchange(DefaultExchange);
        Kind = DefaultKind;

        MaxFileSize = 1L << 31;
        BufferCount = 64;
    }
}

[Singleton]
public class BinanceHttpKlineDataWriter : DataWriter<BinanceHttpKline,
    BinanceHttpKline, BinanceHttpKlineDoubleSerializerConverter, BinanceHttpKlineDataWriterOptions>
{
    public BinanceHttpKlineDataWriter(ILogger logger, IConfiguration configuration, IServiceProvider serviceProvider) :
        base(
            logger,
            configuration.GetSection("Binance:Collector:Kline:Writer"), serviceProvider) { }
}

[Singleton]
public class BinanceFuturesHttpKlineDataWriter : DataWriter<BinanceHttpKline,
    BinanceHttpKline, BinanceHttpKlineDoubleSerializerConverter, BinanceHttpKlineDataWriterOptions>
{
    public BinanceFuturesHttpKlineDataWriter(ILogger logger, IConfiguration configuration,
        IServiceProvider serviceProvider) :
        base(
            logger,
            configuration.GetSection("BinanceFutures:Collector:Kline:Writer"), serviceProvider) { }
}

public class BinanceSpotFuturesHttpKlineDataWriterUnion
{
    internal BinanceHttpKlineDataWriter? Binance { get; }
    internal BinanceFuturesHttpKlineDataWriter? BinanceFutures { get; }

    internal BinanceSpotFuturesHttpKlineDataWriterUnion(BinanceHttpKlineDataWriter dataWriter)
    {
        Binance = dataWriter;
    }

    internal BinanceSpotFuturesHttpKlineDataWriterUnion(BinanceFuturesHttpKlineDataWriter dataWriter)
    {
        BinanceFutures = dataWriter;
    }

    public async Task WriteAsync<TCollection>(string symbol, TCollection data, DateTimeOffset datetime,
        CancellationToken cancellationToken) where TCollection : IReadOnlyCollection<BinanceHttpKline>
    {
        if (Binance is not null)
        {
            await Binance.WriteAsync(symbol, data, datetime, cancellationToken);
        }

        if (BinanceFutures is not null)
        {
            await BinanceFutures.WriteAsync(symbol, data, datetime, cancellationToken);
        }
    }

    public async Task Clear(CancellationToken cancellationToken)
    {
        if (Binance is not null)
        {
            await Binance.Clear(cancellationToken: cancellationToken);
        }

        if (BinanceFutures is not null)
        {
            await BinanceFutures.Clear(cancellationToken: cancellationToken);
        }
    }

    public async Task Flush(bool createWriter = true, CancellationToken cancellationToken = default)
    {
        if (Binance is not null)
        {
            await Binance.Flush(createWriter, cancellationToken: cancellationToken);
        }

        if (BinanceFutures is not null)
        {
            await BinanceFutures.Flush(createWriter, cancellationToken: cancellationToken);
        }
    }

    public DataWriterOptions Options
    {
        get
        {
            if (Binance is not null)
            {
                return Binance.Options;
            }

            if (BinanceFutures is not null)
            {
                return BinanceFutures.Options;
            }

            throw new NullReferenceException("neither binance spot or futures are set");
        }
    }
}

public record BinanceKlineCollectorSymbolContext(string Symbol,
    DateTimeOffset LastProcessedTime = default,
    DateTimeOffset FileTime = default);

public interface IBinanceKlineCollector : IAsyncDisposable
{
    Task Collect(CancellationToken cancellationToken);
}

public class BinanceKlineCollectorOptions
{
    public long StartTime { get; set; } = 1577836800000; // 2020-01-01

    public int RateLimiterSafePercent { get; set; } = 85;

    public bool AutoFlush { get; set; } = true;
}

public interface IBinanceKlineCollectorAdapter
{
    IConfigurationSection GetConfigurationSection(IConfiguration configuration);
    ValueTask<DateTimeOffset> RemoteTime(CancellationToken cancellationToken);
    Task<ICollection<string>> ListSymbols(CancellationToken cancellationToken);
    Task<IPairFilter> GetPairFilter(CancellationToken cancellationToken);
    IBinanceRateLimiter RateLimiter { get; }

    BinanceSpotFuturesHttpKlineDataWriterUnion DataWriter { get; }

    Task<PooledList<BinanceHttpKline>> GetKlines(string symbol,
        string interval = "1m",
        long? startTime = null,
        long? endTime = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    int OptimalKlineLimit { get; }
}

public struct BinanceSpotKlineCollectorAdapter : IBinanceKlineCollectorAdapter
{
    public required IBinancePublicHttpApi BinancePublicHttpApi { get; set; }
    public required IPairFilterLoader PairFilterLoader { get; set; }

    public required BinanceSpotFuturesHttpKlineDataWriterUnion DataWriter { get; set; }

    public IConfigurationSection GetConfigurationSection(IConfiguration configuration) =>
        configuration.GetSection("Binance:Collector:Kline");

    public async ValueTask<DateTimeOffset> RemoteTime(CancellationToken cancellationToken) =>
        (await BinancePublicHttpApi.GetServerTime(cancellationToken: cancellationToken).ConfigureAwait(false))
        .DateTimeOffset;

    public async Task<ICollection<string>> ListSymbols(CancellationToken cancellationToken) =>
        await BinancePublicHttpApi.ListSymbols(true, checkStatus: true, cancellationToken: cancellationToken);

    public async Task<IPairFilter> GetPairFilter(CancellationToken cancellationToken) =>
        await PairFilterLoader.GetPairFilterAsync("Binance:Collector:Kline", cancellationToken)
            .ConfigureAwait(false);

    public IBinanceRateLimiter RateLimiter => BinancePublicHttpApi.RateLimiter;

    public async Task<PooledList<BinanceHttpKline>> GetKlines(string symbol,
        string interval = "1m",
        long? startTime = null,
        long? endTime = null,
        int? limit = null,
        CancellationToken cancellationToken = default) =>
        await BinancePublicHttpApi.GetKlines(symbol, interval, startTime, endTime, limit ?? OptimalKlineLimit,
            cancellationToken: cancellationToken);

    public int OptimalKlineLimit => IBinancePublicHttpApi.MaxKlineLimit;
}

public struct BinanceFuturesKlineCollectorAdapter : IBinanceKlineCollectorAdapter
{
    public required IBinanceFuturesPublicHttpApi HttpApi { get; set; }
    public required IPairFilterLoader PairFilterLoader { get; set; }

    public required BinanceSpotFuturesHttpKlineDataWriterUnion DataWriter { get; set; }

    public IConfigurationSection GetConfigurationSection(IConfiguration configuration) =>
        configuration.GetSection("BinanceFutures:Collector:Kline");

    public async ValueTask<DateTimeOffset> RemoteTime(CancellationToken cancellationToken) =>
        (await HttpApi.GetServerTime(cancellationToken: cancellationToken).ConfigureAwait(false))
        .DateTimeOffset;

    public async Task<ICollection<string>> ListSymbols(CancellationToken cancellationToken) =>
        await HttpApi.ListSymbols(true, checkStatus: true, cancellationToken: cancellationToken);

    public async Task<IPairFilter> GetPairFilter(CancellationToken cancellationToken) =>
        await PairFilterLoader.GetPairFilterAsync("BinanceFutures:Collector:Kline", cancellationToken)
            .ConfigureAwait(false);

    public IBinanceRateLimiter RateLimiter => HttpApi.RateLimiter;

    public async Task<PooledList<BinanceHttpKline>> GetKlines(string symbol,
        string interval = "1m",
        long? startTime = null,
        long? endTime = null,
        int? limit = null,
        CancellationToken cancellationToken = default) =>
        await HttpApi.GetKlines(symbol, interval, startTime, endTime, limit ?? OptimalKlineLimit,
            cancellationToken: cancellationToken);

    public int OptimalKlineLimit => IBinanceFuturesPublicHttpApi.OptimalKlineLimit;
}

public abstract class BaseBinanceKlineCollector<TBinanceKlineCollectorAdapter>
    where TBinanceKlineCollectorAdapter : IBinanceKlineCollectorAdapter
{
    private readonly ILogger _logger;
    private readonly IPathResolver _pathResolver;
    private readonly ConcurrentDictionary<string, BinanceKlineCollectorSymbolContext> _contexts = new();
    private readonly BinanceKlineCollectorOptions _options = new();
    protected readonly TBinanceKlineCollectorAdapter Adapter;


    protected BaseBinanceKlineCollector(ILogger logger, IPathResolver pathResolver, IConfiguration configuration,
        TBinanceKlineCollectorAdapter adapter)
    {
        _logger = logger.ForContext(GetType());
        Adapter = adapter;
        _pathResolver = pathResolver;
        adapter.GetConfigurationSection(configuration).Bind(_options);
    }


    public async Task Collect(CancellationToken cancellationToken)
    {
        var symbolsTask = Adapter.ListSymbols(cancellationToken: cancellationToken);
        var pairFilter = await Adapter.GetPairFilter(cancellationToken).ConfigureAwait(false);
        var symbols = await symbolsTask.ConfigureAwait(false);
        var rateLimiter = Adapter.RateLimiter;
        var time = await Adapter.RemoteTime(cancellationToken).ConfigureAwait(false);
        await Parallel.ForEachAsync(
            symbols.Where(symbol => pairFilter.Match(symbol)), cancellationToken, async (symbol, _) =>
            {
                if (_options.RateLimiterSafePercent > 0 &&
                    rateLimiter.AvailableWeight * 100 / rateLimiter.MaxUsableWeight > _options.RateLimiterSafePercent)
                {
                    _logger.Verbose("Skipping kline update for {Symbol} as rate limiter usage is high", symbol);
                    return;
                }

                DateTimeOffset lastProcessedTime;
                try
                {
                    lastProcessedTime = await GetLastProcessedTime(symbol, cancellationToken).ConfigureAwait(false);
                }
                catch (IOException)
                {
                    await Adapter.DataWriter.Clear(cancellationToken: cancellationToken).ConfigureAwait(false);
                    lastProcessedTime = await GetLastProcessedTime(symbol, cancellationToken).ConfigureAwait(false);
                }

                if (lastProcessedTime == default)
                {
                    lastProcessedTime = DateTimeOffset.FromUnixTimeMilliseconds(_options.StartTime - 60_001);
                }

                if ((lastProcessedTime - time).Duration() < TimeSpan.FromMinutes(1))
                {
                    _logger.Verbose("No need to load kline for {Symbol}", symbol);
                    return;
                }

                var limit = Adapter.OptimalKlineLimit;
                var endTime = lastProcessedTime + TimeSpan.FromMinutes(limit + 1) - TimeSpan.FromMilliseconds(1);
                var klines = await Adapter.GetKlines(
                    symbol,
                    startTime: lastProcessedTime.ToUnixTimeMilliseconds(),
                    endTime: endTime.ToUnixTimeMilliseconds(),
                    limit: limit,
                    cancellationToken: cancellationToken);

                Debug.Assert(klines.IsSorted(new KlineOpenTimeComparer()));
                Debug.Assert(klines.IsSorted(new KlineCloseTimeComparer()));

                if (klines.Count == 0 && (endTime - time).Duration() > TimeSpan.FromDays(1))
                {
                    _contexts.AddOrUpdate(symbol,
                        s => new BinanceKlineCollectorSymbolContext(s, endTime, endTime),
                        (_, prev) => prev with { LastProcessedTime = endTime, FileTime = endTime });
                    return;
                }

                static bool RemoveTail(PooledList<BinanceHttpKline> klines)
                {
                    if (klines.Count == 1)
                    {
                        ref var data = ref klines.Front();
                        return data.CloseTime - data.OpenTime < 59999;
                    }

                    ref var first = ref klines.Front();
                    ref var last = ref klines.Back();

                    return (first.CloseTime - first.OpenTime) > (last.CloseTime - first.OpenTime);
                }

                if (klines.Count > 0 && RemoveTail(klines))
                {
                    klines.Resize(klines.Count - 1, false);
                }

                static int FilterTime(PooledList<BinanceHttpKline> klines, long time)
                {
                    var c = 0;
                    while (klines.Count > 0 && klines.Front().CloseTime <= time)
                    {
                        klines.RemoveAt(0);
                        c++;
                    }

                    return c;
                }

                FilterTime(klines, lastProcessedTime.ToUnixTimeMilliseconds());

                var fileTime = DateTimeOffset.FromUnixTimeMilliseconds(klines[0].OpenTime);
                if (_contexts.TryGetValue(symbol, out var context) && context.FileTime != default)
                {
                    fileTime = context.FileTime;
                }

                if (klines.Count > 0)
                {
                    await Adapter.DataWriter.WriteAsync(symbol,
                        klines,
                        fileTime,
                        cancellationToken);

                    lastProcessedTime = DateTimeOffset.FromUnixTimeMilliseconds(klines.Back().CloseTime);
                    _contexts.AddOrUpdate(symbol,
                        s => new BinanceKlineCollectorSymbolContext(s, lastProcessedTime, fileTime),
                        (_, prev) => prev with { LastProcessedTime = lastProcessedTime, FileTime = fileTime }
                    );
                }
            });

        if (_options.AutoFlush)
        {
            await Adapter.DataWriter.Flush(true, cancellationToken).ConfigureAwait(false);
        }
    }

    // ReSharper disable once UnusedType.Local
    private struct KlineOpenTimeComparer : IComparer<BinanceHttpKline>
    {
        public int Compare(BinanceHttpKline x, BinanceHttpKline y) => x.OpenTime.CompareTo(y.OpenTime);
    }

    // ReSharper disable once UnusedType.Local
    private struct KlineCloseTimeComparer : IComparer<BinanceHttpKline>
    {
        public int Compare(BinanceHttpKline x, BinanceHttpKline y) => x.CloseTime.CompareTo(y.CloseTime);
    }

    private async ValueTask<DateTimeOffset> GetLastProcessedTime(string symbol, CancellationToken cancellationToken)
    {
        if (_contexts.TryGetValue(symbol, out var context) && context.LastProcessedTime != default)
        {
            return context.LastProcessedTime;
        }

        return await LoadLastProcessedTime(symbol, cancellationToken);
    }

    private async ValueTask<DateTimeOffset> LoadLastProcessedTime(string symbol, CancellationToken cancellationToken)
    {
        var sampleFile = Adapter.DataWriter.Options.FormatFile(symbol, "sample");
        sampleFile = _pathResolver.Resolve(sampleFile);
        var dir = Directory.GetParent(sampleFile);
        if (dir is null || !dir.Exists)
        {
            return default;
        }

        var files = dir.EnumerateFiles($"*.{Adapter.DataWriter.Options.Extension}", SearchOption.TopDirectoryOnly);

        static DateTimeOffset ReadDateTimeOffset(Stream fs)
        {
            BinanceHttpKline kline = default;
            var klineSize = kline.ExpectedSize;
            const int stackSize = 16;
            var buffer = stackSize >= klineSize ? stackalloc double[stackSize] : new double[klineSize];
            var blockByteSize = klineSize * sizeof(double);
            if (!fs.CanRead)
            {
                throw new ArgumentException("unable to seek");
            }

            if (fs.Length % blockByteSize != 0)
            {
                throw new ArgumentException("miss aligned data");
            }

            fs.Seek(-blockByteSize, SeekOrigin.End);
            if (fs.Read(MemoryMarshal.Cast<double, byte>(buffer)[..blockByteSize]) < blockByteSize)
            {
                throw new ArgumentException("miss aligned data");
            }

            return DateTimeOffset.FromUnixTimeMilliseconds(checked((long)buffer[0]));
        }

        DateTimeOffset time = default;
        foreach (var file in files.OrderByDescending(info => info.Name))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var fs = file.OpenRead();
            try
            {
                time = ReadDateTimeOffset(fs);
            }
            catch (ArgumentException e)
            {
                _logger.Warning(e, "Unable to read/seek file {File}", file.FullName);
                continue;
            }

            if (time == default || time.ToUnixTimeMilliseconds() < _options.StartTime)
            {
                continue;
            }

            break;
        }

        return time;
    }

    protected virtual async ValueTask DisposeAsync(bool disposing)
    {
        if (disposing)
        {
            await Adapter.DataWriter.Clear(default);
        }
    }

    public ValueTask DisposeAsync() => DisposeAsync(true);
}

// ReSharper disable once UnusedType.Global
public class BinanceKlineCollector : BaseBinanceKlineCollector<BinanceSpotKlineCollectorAdapter>, IBinanceKlineCollector
{
    public BinanceKlineCollector(ILogger logger, IPathResolver pathResolver, IConfiguration configuration,
        BinanceHttpKlineDataWriter dataWriter, IPairFilterLoader pairFilterLoader,
        IBinancePublicHttpApi binancePublicHttpApi) :
        base(logger, pathResolver, configuration,
            new BinanceSpotKlineCollectorAdapter()
            {
                DataWriter = new BinanceSpotFuturesHttpKlineDataWriterUnion(dataWriter),
                PairFilterLoader = pairFilterLoader,
                BinancePublicHttpApi = binancePublicHttpApi
            }) { }
}