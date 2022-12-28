using System.Runtime.CompilerServices;
using Maxisoft.Utils.Logic;

namespace Cryptodd.Utils;

public sealed class ReferenceCounterDisposable<T> : IDisposable where T : IDisposable
{
    private int _counter;
    private readonly AtomicBoolean _disposed = new AtomicBoolean();
    private readonly T _value;

    public T Value => _value;

    public ref readonly T ValueRef => ref _value;

    public bool DisposeOnDeletion { get; set; }


    internal ReferenceCounterDisposable(T value, int counter)
    {
        _value = value;
        _counter = counter;
    }

    public ReferenceCounterDisposable(T value) : this(value, 0) { }

    public int Counter
    {
        get
        {
            Interlocked.MemoryBarrier();
            return _counter;
        }
    }

    public int Increment() => Interlocked.Increment(ref _counter);

    public int Decrement()
    {
        var res = Interlocked.Decrement(ref _counter);
        if (res <= 0)
        {
            Dispose();
        }

        return res;
    }

    public readonly struct DecrementOnDispose : IDisposable
    {
        private readonly ReferenceCounterDisposable<T> _rc;

        internal DecrementOnDispose(ReferenceCounterDisposable<T> rc)
        {
            _rc = rc;
        }

        public void Dispose()
        {
            _rc.Decrement();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DecrementOnDispose NewDecrementOnDispose(bool increment = true)
    {
        if (increment)
        {
            Increment();
        }

        return new DecrementOnDispose(this);
    }


    public static ReferenceCounterDisposable<T> operator ++(in ReferenceCounterDisposable<T> r)
    {
        r.Increment();
        return r;
    }

    public static ReferenceCounterDisposable<T> operator --(in ReferenceCounterDisposable<T> r)
    {
        r.Decrement();
        return r;
    }

    private void ReleaseUnmanagedResources()
    {
        if (DisposeOnDeletion && _disposed.FalseToTrue())
        {
            ValueRef.Dispose();
        }
    }

    ~ReferenceCounterDisposable()
    {
        ReleaseUnmanagedResources();
    }

    public void Dispose()
    {
        ReleaseUnmanagedResources();
        if (_disposed.FalseToTrue())
        {
            ValueRef.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}