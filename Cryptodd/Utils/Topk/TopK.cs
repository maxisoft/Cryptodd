using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Maxisoft.Utils.Collections.Lists;
using Towel;
using Towel.DataStructures;

namespace Cryptodd.Utils.Topk;

public abstract class TopK<T, TComparer, THeap>: IEnumerable<T> where TComparer : IComparer<T> where THeap : IHeap<T>
{
    private readonly TComparer _comparer;
    public TComparer Comparer => _comparer;
    private readonly int _k;
    protected THeap Heap;

    public int K => _k;

    public TopK(int k, TComparer comparer)
    {
        if (k <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(k), k, "k need to be positive integer");
        }

        _k = k;
        _comparer = comparer;
    }

    public T[] ToArray()
    {
        var res = new T[K];
        var len = Heap.CopyTo(res);
        return len != K ? res[..len] : res;
    }

    public void Add(in T value)
    {
        Heap.Add(in value);
    }

    public int Count => Heap.Count;
    
    public IEnumerator<T> GetEnumerator() => Heap.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public class TopK<T, TComparer> : TopK<T, TComparer, RedBlackTreeHeap<T, TComparer>> where TComparer : IComparer<T>
{
    public TopK(int k, TComparer comparer) : base(k, comparer)
    {
        Heap = new RedBlackTreeHeap<T, TComparer>(this);
    }
}

public class TopK<T> : TopK<T, Comparer<T>>
{
    public TopK(int k) : base(k, Comparer<T>.Default) { }
    public TopK(int k, Comparer<T> comparer) : base(k, comparer) { }
}

public interface IHeap<T>: IEnumerable<T>
{
    public int Count { get; }
    public void Add(in T value);
    public int CopyTo(Span<T> span);
}

public struct RedBlackTreeHeap<T, TComparer> : IHeap<T> where TComparer : IComparer<T>
{
    private struct InternalComparer : IFunc<T, T, CompareResult>
    {
        private readonly TComparer _comparer;

        public InternalComparer(TComparer comparer)
        {
            _comparer = comparer;
        }

        public CompareResult Invoke(T arg1, T arg2)
        {
            var res = _comparer.Compare(arg1, arg2);
            return res > 0 ? CompareResult.Greater : (res < 0 ? CompareResult.Less : CompareResult.Equal);
        }
    }
    
    private readonly RedBlackTreeLinked<T, InternalComparer> _tree;
    private readonly int _k;
    private readonly TComparer _comparer;
    private T? _minValue = default;

    internal RedBlackTreeHeap(TopK<T, TComparer> topK)
    {
        _k = topK.K;
        _comparer = topK.Comparer;
        _tree = new RedBlackTreeLinked<T, InternalComparer>(new InternalComparer(topK.Comparer));
    }

    public int Count => _tree.Count;

    public void Add(in T value)
    {
        var count = Count;
        bool recheckMinValue, insert;
        insert = count < _k;
        if (!insert)
        {
            insert = _comparer.Compare(value, _minValue!) > 0;
            recheckMinValue = insert;
        }
        else
        {
            recheckMinValue = count == _k - 1;
        }

        if (!insert)
        {
            return;
        }

        [Conditional("DEBUG")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ThrowOnFail(bool success, Exception? exception, string message)
        {
            if (!success)
            {
                if (exception is not null)
                {
                    throw new ArgumentException(message, nameof(value), exception);
                }

                throw new ArgumentException(message, nameof(value));
            }
        }

        // ReSharper disable RedundantAssignment
        var (success, exception) = _tree.TryAdd(value);

        ThrowOnFail(success, exception, "unable to insert value");

        if (Count > _k)
        {
            (success, exception) = _tree.TryRemove(_minValue!);
            // ReSharper restore RedundantAssignment
            ThrowOnFail(success, exception, "unable to remove value");
        }

        if (recheckMinValue)
        {
            _minValue = _tree.CurrentLeast;
        }

    }

    public int CopyTo(Span<T> span)
    {
        var i = 0;
        foreach (var elem in _tree)
        {
            span[i] = elem;
            i++;
        }

        //span[..i].Sort(_comparer);

        return i;
    }

    public IEnumerator<T> GetEnumerator() => _tree.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// 1.5 * N * LogN complexity for Add() => really bad for large array, good for small ones
/// </summary>
/// <typeparam name="T"></typeparam>
/// <typeparam name="TComparer"></typeparam>
public struct ArrayHeap<T, TComparer> : IHeap<T> where TComparer : IComparer<T>
{
    private readonly ArrayList<T> _content = new();
    private bool _isSorted = false;
    private readonly int _k;
    private readonly TComparer _comparer;

    internal ArrayHeap(TopK<T, TComparer> topK)
    {
        _k = topK.K;
        _content = new ArrayList<T>();
        _content.EnsureCapacity(_k + 1);
        _comparer = topK.Comparer;
        _isSorted = true;
    }

    private void AddNotFull(in T value)
    {
        Debug.Assert(_content.Count < _k);
        if (_content.Count > 0)
        {
            ref var tail = ref GetTail();
            if (_comparer.Compare(value, tail) >= 0)
            {
                _content.Add(value);
            }
            else if (_isSorted)
            {
                _content.AddSorted(in value, _comparer);
            }
            else
            {
                _content.Insert(_content.Count - 1, value);
                _isSorted = false;
            }
        }
        else
        {
            _content.Add(value);
            _isSorted = true;
        }
    }

    public int Count => _content.Count;

    public void Add(in T value)
    {
        if (_content.Count < _k)
        {
            AddNotFull(in value);
            return;
        }


        if (_isSorted)
        {
            ref var tail = ref GetTail();
            if (_comparer.Compare(value, tail) >= 0)
            {
                _content.RemoveAt(0);
                _content.AddSorted(value);
                return;
            }
        }

        EnsureSorted();
        _content.AddSorted(value);
        _content.RemoveAt(0);
    }

    public int CopyTo(Span<T> span)
    {
        _content.AsSpan().CopyTo(span);
        return Count;
    }

    public Span<T> AsSpan() => _content.AsSpan();

    private bool EnsureSorted()
    {
        if (_isSorted)
        {
            return true;
        }

        _content.Sort(_comparer);
        _isSorted = true;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref T GetTail() => ref _content[^1];

    public IEnumerator<T> GetEnumerator()
    {
        EnsureSorted();
        for (var index = 0; index < _content.Count; index++)
        {
            var c = _content[index];
            yield return c;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}