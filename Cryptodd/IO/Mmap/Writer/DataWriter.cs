using System.Collections.Concurrent;
using Cryptodd.IO.FileSystem;
using Cryptodd.IO.FileSystem.OpenedFileLimiter;
using Cryptodd.Json;
using Maxisoft.Utils.Logic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Cryptodd.IO.Mmap.Writer;

public class DataWriter<TIn, TOut, TConverter, TOptions> : IDisposable, IAsyncDisposable
    where TConverter : struct, IDoubleSerializerConverter<TIn, TOut>
    where TOut : IDoubleSerializable
    where TOptions : DataWriterOptions, new()
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;
    public TOptions Options { get; protected set; } = new();
    private readonly ConcurrentDictionary<string, DataBuffer> _buffers = new();
    private readonly ConcurrentDictionary<string, InternalWriterHandler<TOut>> _writers = new();
    private readonly AtomicBoolean _resetRequested = new();
    private IDisposable? _linkedDisposable;

    public DataWriter(ILogger logger, IConfiguration configuration, IServiceProvider serviceProvider)
    {
        _logger = logger.ForContext(GetType());
        configuration.Bind(Options);
        _linkedDisposable = configuration.GetReloadToken().RegisterChangeCallback(_ =>
        {
            configuration.Bind(Options);
            _resetRequested.FalseToTrue();
        }, this);
        _serviceProvider = serviceProvider;
    }

    public async Task WriteAsync<TCollection>(string symbol, TCollection data, DateTimeOffset datetime,
        CancellationToken cancellationToken) where TCollection : IReadOnlyCollection<TIn>
    {
        if (_resetRequested.TrueToFalse())
        {
            await Clear(true, cancellationToken);
        }

        var converter = new TConverter();

        var buffer = _buffers.GetOrAdd(symbol, _ => new DataBuffer { Capacity = Options.BufferCount });

        if (buffer.Add<TCollection, TIn, TOut, TConverter>(data, datetime.ToUnixTimeMilliseconds(), in converter))
        {
            var writer = _writers.GetOrAdd(symbol, WriterValueFactory);
            await buffer.DrainTo<InternalWriterHandler<TOut>, TOut>(writer, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task WriteAsync(string symbol, TIn value, DateTimeOffset datetime,
        CancellationToken cancellationToken) =>
        await WriteAsync(symbol, new OneItemList<TIn>(value), datetime, cancellationToken);

    private InternalWriterHandler<TOut> WriterValueFactory(string symbol) =>
        new(symbol, Options, _serviceProvider.GetRequiredService<IPathResolver>(),
            _serviceProvider.GetRequiredService<ILogger>(), _serviceProvider.GetRequiredService<IDefaultOpenedFileLimiter>());

    private void ReleaseUnmanagedResources()
    {
        _linkedDisposable?.Dispose();
        _linkedDisposable = null;
    }

    protected virtual void Dispose(bool disposing)
    {
        ReleaseUnmanagedResources();
        if (disposing)
        {
            foreach (var (symbol, buffer) in _buffers)
            {
                if (buffer.Count == 0)
                {
                    buffer.Dispose();
                    continue;
                }

                if (_writers.TryGetValue(symbol, out var writer))
                {
                    buffer.DrainTo<InternalWriterHandler<TOut>, TOut>(writer, CancellationToken.None).Wait();
                }

                buffer.Dispose();
            }

            _buffers.Clear();

            foreach (var writer in _writers.Values)
            {
                writer.Dispose();
            }
        }
    }

    public async ValueTask Flush(bool createWriter = true, CancellationToken cancellationToken = default)
    {
        var tasks = new List<Task<int>>(_buffers.Count);
        foreach (var (symbol, buffer) in _buffers)
        {
            if (buffer.Count == 0)
            {
                continue;
            }

            InternalWriterHandler<TOut>? writer;
            if (createWriter)
            {
                writer = _writers.GetOrAdd(symbol, WriterValueFactory);
            }
            else
            {
                _writers.TryGetValue(symbol, out writer);
            }

            if (writer is not null)
            {
                tasks.Add(buffer.DrainTo<InternalWriterHandler<TOut>, TOut>(writer, cancellationToken));
            }
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public async ValueTask Clear(bool createWriter = true, CancellationToken cancellationToken = default)
    {
        await Flush(createWriter, cancellationToken).ConfigureAwait(false);
        foreach (var buffer in _buffers.Values)
        {
            buffer.Dispose();
        }

        _buffers.Clear();
        await Task.WhenAll(_writers.Values.Select(wr => wr.DisposeAsync().AsTask())).ConfigureAwait(false);
        _writers.Clear();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~DataWriter()
    {
        Dispose(false);
    }

    protected virtual async ValueTask DisposeAsync(bool disposing)
    {
        ReleaseUnmanagedResources();
        if (disposing)
        {
            await Clear(false, CancellationToken.None);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsync(true);
        GC.SuppressFinalize(this);
    }
}