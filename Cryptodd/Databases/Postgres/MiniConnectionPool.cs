using System.Collections.Concurrent;
using System.Data;
using Maxisoft.Utils.Collections.Queues.Specialized;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Serilog;

namespace Cryptodd.Databases.Postgres;

public interface IRentedConnection : IDisposable, IAsyncDisposable
{
    NpgsqlConnection Connection { get; }
}

public readonly struct RentedConnection : IRentedConnection
{
    private readonly MiniConnectionPool _pool;
    public NpgsqlConnection Connection { get; }

    internal RentedConnection(NpgsqlConnection connection, MiniConnectionPool pool)
    {
        Connection = connection;
        _pool = pool;
    }

    public void Dispose()
    {
        _pool.Return(Connection);
    }

    public ValueTask DisposeAsync() => _pool.ReturnAsync(Connection);
}

public interface IMiniConnectionPool : IDisposable, IAsyncDisposable
{
    ValueTask<IRentedConnection> RentAsync(CancellationToken cancellationToken);
    void Return(NpgsqlConnection connection);
    ValueTask ReturnAsync(NpgsqlConnection connection);
    
    long CappedSize { get; }
}

public interface IDynamicMiniConnectionPool : IMiniConnectionPool
{
    void ChangeCapSize(int newCappedSize);
}

public sealed class MiniConnectionPool : IDynamicMiniConnectionPool
{
    public const int DefaultCap = 4;

    private readonly ConcurrentQueue<TaskCompletionSource> _awaiters = new();
    private readonly object _lockObject = new();
    private readonly ILogger _logger;
    private readonly IServiceProvider _serviceProvider;
    private BoundedDeque<NpgsqlConnection> _freeConnections;
    private BoundedDeque<NpgsqlConnection> _usedConnections;

    public MiniConnectionPool(IServiceProvider serviceProvider) : this(serviceProvider,
        serviceProvider.GetRequiredService<ILogger>(),
        serviceProvider.GetService<IConfiguration>()?.GetSection("Postgres")?.GetValue("PoolCap", DefaultCap) ??
        DefaultCap) { }

    internal MiniConnectionPool(IServiceProvider serviceProvider, ILogger logger, int cap = DefaultCap)
    {
        _serviceProvider = serviceProvider;
        _logger = logger.ForContext(GetType());
        _freeConnections = new BoundedDeque<NpgsqlConnection>(cap);
        _usedConnections = new BoundedDeque<NpgsqlConnection>(cap);
    }

    public long CappedSize => _usedConnections.CappedSize;

    public long Size => _freeConnections.Count + _usedConnections.Count;

    public bool IsFull => Size >= CappedSize;


    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection[] copy;
        lock (_lockObject)
        {
            copy = _freeConnections.ToArray();
            _freeConnections.Clear();
        }

        foreach (var connection in copy)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        lock (_lockObject)
        {
            copy = _usedConnections.ToArray();
            _usedConnections.Clear();
        }

        if (copy.Any(connection => connection.State != ConnectionState.Closed))
        {
            _logger.Warning("There's still {Count} Used connection", copy.Length);
        }

        foreach (var connection in copy)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        while (_awaiters.TryDequeue(out var awaiter))
        {
            awaiter.TrySetCanceled();
        }

        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        lock (_lockObject)
        {
            while (_freeConnections.TryPopFront(out var connection))
            {
                connection.Dispose();
            }
        }

        lock (_lockObject)
        {
            if (_usedConnections.Any(connection => connection.State != ConnectionState.Closed))
            {
                _logger.Warning("There's still {Count} Used connection", _usedConnections.Count);
            }

            while (_usedConnections.TryPopFront(out var connection))
            {
                connection.Dispose();
            }
        }

        while (_awaiters.TryDequeue(out var awaiter))
        {
            awaiter.TrySetCanceled();
        }

        GC.SuppressFinalize(this);
    }

    public void ChangeCapSize(int newCappedSize)
    {
        lock (_lockObject)
        {
            if (!_awaiters.IsEmpty || _freeConnections.Any() || _usedConnections.Any())
            {
                throw new InvalidOperationException("Cannot change cap size once this object is in use");
            }

            _freeConnections = new BoundedDeque<NpgsqlConnection>(newCappedSize);
            _usedConnections = new BoundedDeque<NpgsqlConnection>(newCappedSize);
        }
    }

    public async ValueTask<IRentedConnection> RentAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_freeConnections.Any())
            {
                lock (_lockObject)
                {
                    if (!_usedConnections.IsFull && _freeConnections.TryPopFront(out var connection))
                    {
                        _usedConnections.Add(connection);
                        return new RentedConnection(connection, this);
                    }
                }
            }

            if (!IsFull)
            {
                var notifyAndContinue = false;
                lock (_lockObject)
                {
                    if (!IsFull)
                    {
                        _freeConnections.Add(_serviceProvider.GetRequiredService<NpgsqlConnection>());
                        notifyAndContinue = true;
                    }
                }

                if (notifyAndContinue)
                {
                    NotifyAwaiters();
                    continue;
                }
            }

            TaskCompletionSource tcs;
            lock (_lockObject)
            {
                if (!IsFull)
                {
                    continue;
                }

                tcs = new TaskCompletionSource();
                _awaiters.Enqueue(tcs);
            }

            await using var _ = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            await tcs.Task.ConfigureAwait(true);
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new OperationCanceledException();
    }

    public void Return(NpgsqlConnection connection)
    {
        RemoveUsedConnection(connection);

        if (IsFull)
        {
            connection.Dispose();
        }
        else
        {
            AddToFreeConnections(connection);
            NotifyAwaiters();
        }
    }

    public ValueTask ReturnAsync(NpgsqlConnection connection)
    {
        RemoveUsedConnection(connection);
        if (IsFull)
        {
            lock (_lockObject)
            {
                if (IsFull)
                {
                    return connection.DisposeAsync();
                }
            }
        }

        AddToFreeConnections(connection);
        NotifyAwaiters();
        return ValueTask.CompletedTask;
    }

    private void RemoveUsedConnection(NpgsqlConnection connection)
    {
        lock (_lockObject)
        {
            if (!_usedConnections.Remove(connection))
            {
                throw new ArgumentException("Trying to return a non used connection", nameof(connection));
            }
        }
    }

    private void NotifyAwaiters()
    {
        while (_awaiters.TryDequeue(out var awaiter))
        {
            awaiter.TrySetResult();
        }
    }

    private void AddToFreeConnections(NpgsqlConnection connection)
    {
        if (connection.State != ConnectionState.Open)
        {
            return;
        }

        lock (_lockObject)
        {
            _freeConnections.PushFront(connection);
        }
    }
#if DEBUG
    ~MiniConnectionPool()
    {
        if (_usedConnections.Any())
        {
            _logger.Error("Non disposed {Type}", GetType());
        }
        else if (_freeConnections.Any(connection => connection.State != ConnectionState.Closed))
        {
            _logger.Error("Non disposed {Type}", GetType());
        }
    }
#endif
}