using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Cryptodd.Pairs;
using Microsoft.CodeAnalysis;
using Microsoft.Win32.SafeHandles;

namespace Cryptodd.OrderBooks;

public class WriteAheadLogOptions
{
    public WriteAheadLogOptions(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; set; }
    public string? MapName { get; set; }
}

public interface IFlushCollector
{
    public void Collect(long symbolHash, long timestamp, ReadOnlySpan<float> data);
}

public class WriteAheadLog<TIn, TOut, TConverter> : IDisposable
    where TConverter : struct, IFloatSerializableConverter<TIn, TOut>
    where TOut : IFloatSerializable
{
    public WriteAheadLog(WriteAheadLogOptions options)
    {
        Options = options;
    }

    public WriteAheadLogOptions Options { get; init; }
    public const int FileSize = 16 << 20;
    public const int HeaderLength = 8 * sizeof(long);
    public const int MaxEntryStructToFloatSize = 3;
    private Optional<MemoryMappedFile> _mmap;
    private readonly object _lockObject = new();
    private MemoryMappedViewAccessor? _headerAccessor;
    private readonly ReaderWriterLockSlim _readerWriterLock = new(LockRecursionPolicy.NoRecursion);

    public int Count
    {
        get => GetCount();
        set => SetCount(value, true);
    }

    private int GetCount()
    {
        if (_headerAccessor is null)
        {
            lock (_lockObject)
            {
                OpenMemMap();
                _headerAccessor = _mmap.Value.CreateViewAccessor(0, HeaderLength);
            }
        }

        unsafe
        {
            byte* ptr = default;
            var handle = _headerAccessor.SafeMemoryMappedViewHandle;
            handle.AcquirePointer(ref ptr);
            try
            {
                return checked((int)MemoryMarshal.Cast<byte, long>(new Span<byte>(ptr, HeaderLength))[^1]);
            }
            finally
            {
                handle.ReleasePointer();
            }
        }
    }

    private void SetCount(int count, bool flush = false)
    {
        if (_headerAccessor is null)
        {
            lock (_lockObject)
            {
                OpenMemMap();
                _headerAccessor = _mmap.Value.CreateViewAccessor(0, HeaderLength);
            }
        }

        unsafe
        {
            byte* ptr = default;
            var handle = _headerAccessor.SafeMemoryMappedViewHandle;
            handle.AcquirePointer(ref ptr);
            try
            {
                MemoryMarshal.Cast<byte, long>(new Span<byte>(ptr, HeaderLength))[^1] = count;
            }
            finally
            {
                handle.ReleasePointer();
            }
        }

        if (flush)
        {
            _headerAccessor.Flush();
        }
    }

    public bool TryWrite<TCollection>(string symbol, DateTimeOffset dateTimeOffset, TCollection data)
        where TCollection : ICollection<TIn> =>
        TryWrite(PairHasher.Hash(symbol), dateTimeOffset.ToUnixTimeMilliseconds(), data);

    public bool TryWrite<TCollection>(long symbolHash, long timestamp, TCollection data)
        where TCollection : ICollection<TIn>
    {
        OpenMemMap();
        Span<float> buffer = stackalloc float[MaxEntryStructToFloatSize];
        var size = new TConverter().Convert(data.First()).WriteTo(buffer);
        buffer = buffer[..size];
        MemoryMappedViewAccessor accessor;
        var lockCount = 0;
        do
        {
            lock (_lockObject)
            {
                var count = Count;
                var structSize = (long)size * sizeof(float) * data.Count + 3 * sizeof(long);
                if (HeaderLength + count + structSize > FileSize)
                {
                    return false;
                }

                accessor = _mmap.Value.CreateViewAccessor(HeaderLength + count, structSize);
                if (_readerWriterLock.TryEnterReadLock(100))
                {
                    try
                    {
                        lockCount = -1;
                        SetCount(checked((int)(count + structSize)));
                    }
                    catch
                    {
                        _readerWriterLock.ExitReadLock();
                        throw;
                    }
                }
                else
                {
                    if (lockCount++ > 5)
                    {
                        return false;
                    }
                }
            }
        } while (lockCount > 0);


        try
        {
            try
            {
                var position = 0;
                accessor.Write(position, symbolHash);
                position += sizeof(long);
                accessor.Write(position, timestamp);
                position += sizeof(long);
                accessor.Write(data.Count, timestamp);
                position += sizeof(long);
                foreach (var item in data)
                {
                    size = new TConverter().Convert(item).WriteTo(buffer);
                    for (var i = 0; i < size; i++)
                    {
                        accessor.Write(position, buffer[i]);
                        position += sizeof(float);
                    }
                }
            }
            finally
            {
                _readerWriterLock.ExitReadLock();
            }
        }
        finally
        {
            accessor.Dispose();
        }

        return true;
    }

    public int Flush<TFlushCollector>(TFlushCollector collector, TIn example) where TFlushCollector : IFlushCollector
    {
        if (!_readerWriterLock.TryEnterWriteLock(15_000))
        {
            return -2;
        }

        var res = 0;
        try
        {
            lock (_lockObject)
            {
                var count = Count;
                SetCount(0);
                using var accessor = _mmap.Value.CreateViewAccessor(HeaderLength, FileSize - HeaderLength);
                var position = 0;
                Span<float> buffer = stackalloc float[MaxEntryStructToFloatSize];
                var size = new TConverter().Convert(example).WriteTo(buffer);
                Span<byte> span;
                SafeMemoryMappedViewHandle handle;
                unsafe
                {
                    byte* ptr = default;
                    handle = accessor.SafeMemoryMappedViewHandle;
                    handle.AcquirePointer(ref ptr);
                    try
                    {
                        span = new Span<byte>(ptr, FileSize - HeaderLength);
                    }
                    catch
                    {
                        handle.ReleasePointer();
                        throw;
                    }
                }


                try
                {
                    while (position < count)
                    {
                        var symbolHash = accessor.ReadInt64(position);
                        position += sizeof(long);
                        var timestamp = accessor.ReadInt64(position);
                        accessor.Write(position, (long)-1); // mark as invalid
                        position += sizeof(long);
                        var dataCount = accessor.ReadInt64(position);
                        position += sizeof(long);
                        var nextPosition = checked(position + (int)dataCount * size * sizeof(float));
                        var data = span[position..nextPosition];
                        collector.Collect(symbolHash, timestamp, MemoryMarshal.Cast<byte, float>(data));
                        position = nextPosition;
                        res++;
                    }
                }
                finally
                {
                    handle.ReleasePointer();
                }
            }

            SetCount(0, flush: true);
            return res;
        }
        finally
        {
            _readerWriterLock.ExitWriteLock();
        }
    }

    private void OpenMemMap()
    {
        if (!_mmap.HasValue)
        {
            lock (_lockObject)
            {
                if (!_mmap.HasValue)
                {
                    _mmap = MemoryMappedFile.CreateFromFile(Options.FilePath, FileMode.OpenOrCreate, Options.MapName,
                        FileSize,
                        MemoryMappedFileAccess.ReadWrite);
                }
            }
        }
    }

    private void ReleaseUnmanagedResources()
    {
        _headerAccessor?.Dispose();
        if (!_mmap.HasValue)
        {
            return;
        }

        _mmap.Value.Dispose();
    }

    protected virtual void Dispose(bool disposing)
    {
        ReleaseUnmanagedResources();
        if (disposing)
        {
            _readerWriterLock.Dispose();
            _mmap = default;
            _headerAccessor = null;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~WriteAheadLog()
    {
        Dispose(false);
    }
}