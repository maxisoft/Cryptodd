using System.Buffers;
using System.Runtime.InteropServices;
using Maxisoft.Utils.Collections.Queues;
using Maxisoft.Utils.Collections.Queues.Specialized;

namespace Cryptodd.OrderBooks.Writer;

public sealed class OrderBookBuffer : IDisposable
{
    private readonly record struct OrderBookBufferEntry(long timestamp, byte[] ob, int length);

    // ReSharper disable once InconsistentlySynchronizedField
    public int Count => _buffer.Count;
    public int Capacity { get; init; } = 8;
    private readonly int _maxCapacity;

    private BoundedDeque<OrderBookBufferEntry> _buffer;

    // ReSharper disable once StaticMemberInGenericType
    private static readonly ArrayPool<byte> MemoryPool = ArrayPool<byte>.Create(1024 * sizeof(float), 64);

    public OrderBookBuffer(int maxCapacity = 1024)
    {
        _maxCapacity = maxCapacity;
        _buffer = CreateBuffer(maxCapacity);
    }

    private static BoundedDeque<OrderBookBufferEntry> CreateBuffer(int maxCapacity) =>
        new(maxCapacity, DequeInitialUsage.Fifo);

    public bool Add<TCollection, TIn, TOut, TConverter>(TCollection value, long timestamp, in TConverter converter)
        where TCollection : IReadOnlyCollection<TIn>
        where TOut : IFloatSerializable
        where TConverter : IFloatSerializableConverter<TIn, TOut>
    {
        var buff = MemoryPool.Rent(value.Count * converter.Convert(value.First()).ExpectedSize * sizeof(float));
        var buffSpan = MemoryMarshal.Cast<byte, float>(buff);
        var c = 0;
        try
        {
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var floatSerializable in value)
            {
                c += converter.Convert(floatSerializable).WriteTo(buffSpan[c..]);
            }

            lock (_buffer)
            {
                _buffer.Add(new OrderBookBufferEntry(timestamp, buff, c * sizeof(float)));
                return _buffer.Count >= Capacity;
            }
        }
        catch
        {
            MemoryPool.Return(buff);
            throw;
        }
    }

    public async Task<int> DrainTo<TOrderBookWriter, T>(TOrderBookWriter writer, CancellationToken cancellationToken)
        where TOrderBookWriter : IOrderBookWriterHandler<T> where T : IFloatSerializable
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }

        BoundedDeque<OrderBookBufferEntry> localBuffer;
        lock (_buffer)
        {
            localBuffer = _buffer;
            _buffer = CreateBuffer(_maxCapacity);
        }

        var res = 0;
        try
        {
            await writer.BeginWrite(localBuffer.Count, cancellationToken).ConfigureAwait(false);

            while (localBuffer.TryPopFront(out var serializable))
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    await writer.WriteAsync(((ReadOnlyMemory<byte>)serializable.ob)[..serializable.length],
                            serializable.timestamp, cancellationToken)
                        .ConfigureAwait(false);
                    res += 1;
                }
                finally
                {
                    MemoryPool.Return(serializable.ob);
                }
            }

            await writer.EndWrite(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (localBuffer.Count > 0)
            {
                lock (_buffer)
                {
                    foreach (var serializable in localBuffer)
                    {
                        _buffer.PushFront(in serializable);
                    }
                }
            }
        }

        return res;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (_buffer.Count <= 0)
        {
            return;
        }

        OrderBookBufferEntry[] entries;
        lock (_buffer)
        {
            entries = _buffer.ToArray();
            _buffer.Clear();
        }

        foreach (var (_, bytes, _) in entries)
        {
            MemoryPool.Return(bytes);
        }
    }

    ~OrderBookBuffer()
    {
        Dispose();
    }
}