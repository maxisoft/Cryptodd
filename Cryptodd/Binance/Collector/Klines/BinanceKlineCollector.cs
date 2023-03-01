using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cryptodd.Binance.Http;
using Cryptodd.Binance.Models;
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

[Singleton]
public class BinanceHttpKlineDataWriter : DataWriter<BinanceHttpKline,
    BinanceHttpKline, BinanceHttpKlineDoubleSerializerConverter, BinanceHttpKlineDataWriterOptions>
{
    public BinanceHttpKlineDataWriter(ILogger logger, IConfiguration configuration, IServiceProvider serviceProvider) :
        base(
            logger,
            configuration.GetSection("Binance:Collector:Kline:Writer"), serviceProvider) { }
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

    public int RateLimiterSafePercent = 85;

    public bool AutoFlush { get; set; } = true;
}

public class BinanceKlineCollector : IBinanceKlineCollector
{
    private readonly ILogger _logger;
    private readonly IBinancePublicHttpApi _binancePublicHttpApi;
    private readonly IPairFilterLoader _pairFilterLoader;
    private readonly BinanceHttpKlineDataWriter _dataWriter;
    private readonly IPathResolver _pathResolver;
    private readonly ConcurrentDictionary<string, BinanceKlineCollectorSymbolContext> _contexts = new();
    private readonly BinanceKlineCollectorOptions _options = new();


    public BinanceKlineCollector(ILogger logger, IBinancePublicHttpApi binancePublicHttpApi,
        IPairFilterLoader pairFilterLoader, BinanceHttpKlineDataWriter dataWriter, IPathResolver pathResolver,
        IConfiguration configuration)
    {
        _logger = logger;
        _binancePublicHttpApi = binancePublicHttpApi;
        _pairFilterLoader = pairFilterLoader;
        _dataWriter = dataWriter;
        _pathResolver = pathResolver;
        configuration.GetSection("Binance:Collector:Kline").Bind(_options);
    }

    private async ValueTask<DateTimeOffset> RemoteTime()
    {
        //TODO use binance remote time
        return DateTimeOffset.UtcNow;
    }

    public async Task Collect(CancellationToken cancellationToken)
    {
        var symbolsTask =
            _binancePublicHttpApi.ListSymbols(true, checkStatus: true, cancellationToken: cancellationToken);
        var pairFilter = await _pairFilterLoader.GetPairFilterAsync("Binance:Collector:Kline", cancellationToken)
            .ConfigureAwait(false);
        var symbols = await symbolsTask.ConfigureAwait(false);
        var rateLimiter = _binancePublicHttpApi.RateLimiter;
        var time = await RemoteTime().ConfigureAwait(false);
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
                    await _dataWriter.Clear(cancellationToken: cancellationToken);
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

                const int limit = IBinancePublicHttpApi.MaxKlineLimit;
                var endTime = lastProcessedTime + TimeSpan.FromMinutes(limit + 1) - TimeSpan.FromMilliseconds(1);
                var klines = await _binancePublicHttpApi.GetKlines(
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
                    await _dataWriter.WriteAsync(symbol,
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
            await _dataWriter.Flush(true, cancellationToken).ConfigureAwait(false);
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
        var sampleFile = _dataWriter.Options.FormatFile(symbol, "sample");
        sampleFile = _pathResolver.Resolve(sampleFile);
        var dir = Directory.GetParent(sampleFile);
        if (dir is null || !dir.Exists)
        {
            return default;
        }

        var files = dir.EnumerateFiles($"*.{_dataWriter.Options.Extension}", SearchOption.TopDirectoryOnly);

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

    public async ValueTask DisposeAsync()
    {
        await _dataWriter.Clear();
        GC.SuppressFinalize(this);
    }
}