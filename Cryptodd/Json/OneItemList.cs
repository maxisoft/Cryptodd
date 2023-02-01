using System.Collections;
using System.Runtime.CompilerServices;

namespace Cryptodd.Json;

public struct OneItemList<T> : IList<T>, IReadOnlyList<T>
{
    public OneItemList() : this(default!, 0)
    {
    }

    public OneItemList(T value, int count = 1)
    {
        Value = value;
        Count = count;
    }
    
#pragma warning disable CA1051
    public T Value;
#pragma warning restore CA1051

    public int Capacity => 1;

    public bool HasValue => Count > 0;
    public bool IsEmpty => Count == 0;
    public T? NullableValue => HasValue ? Value : default;

    public T Coalesce(T other) => HasValue ? Value : other;

    public void Add(T item)
    {
        if (Count >= 1)
        {
            throw new ArgumentException("this list can't contains more than 1 item");
        }

        Value = item;
        Count++;
    }

    public void Swap(ref T other)
    {
        (Value, other) = (other, Value);
        Count = 1;
    }

    public Span<T> AsSpan()
    {
        unsafe
        {
            return new Span<T>(Unsafe.AsPointer(ref Value), Count);
        }
    }

    public void Resize(int count)
    {
        if (count is not (0 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "");
        }

        Count = count;
    }

    public void Clear()
    {
        Count = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public bool Equals(in T item) => Count != 0 && EqualityComparer<T>.Default.Equals(item, Value);

    public bool Contains(T item) => Equals(in item);

    public void CopyTo(T[] array, int arrayIndex)
    {
        array[arrayIndex] = Value;
    }

    public bool Remove(T item)
    {
        if (Equals(in item))
        {
            Count = 0;
            return true;
        }

        return false;
    }

    public int Count { get; private set; }
    public bool IsReadOnly => false;

    public IEnumerator<T> GetEnumerator()
    {
        yield return Value;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public int IndexOf(T item) => Equals(in item) ? 0 : -1;

    public void Insert(int index, T item)
    {
        if (Count == 0 && index == 0)
        {
            Value = item;
        }

        throw new ArgumentOutOfRangeException(nameof(index), index, "");
    }

    public void RemoveAt(int index)
    {
        if (Count > 0 && index == 0)
        {
            Count = 0;
        }

        throw new ArgumentOutOfRangeException(nameof(index), index, "");
    }

    public T this[int index]
    {
        get
        {
            if (Count > 0 && index == 0)
            {
                return Value;
            }

            throw new ArgumentOutOfRangeException(nameof(index), index, "");
        }
        set
        {
            if (index == 0)
            {
                Value = value;
            }

            throw new ArgumentOutOfRangeException(nameof(index), index, "");
        }
    }
}