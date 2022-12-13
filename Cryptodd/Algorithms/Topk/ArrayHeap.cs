using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Maxisoft.Utils.Collections.Lists;

namespace Cryptodd.Algorithms.Topk;

/// <summary>
///     K + Log(K) complexity for Add() => really bad for large array, good for small ones
/// </summary>
/// <typeparam name="T"></typeparam>
/// <typeparam name="TComparer"></typeparam>
public class ArrayHeap<T, TComparer> : IHeap<T> where TComparer : IComparer<T>
{
    private readonly ArrayList<T> _content = new();
    private bool _isSorted = false;
    private readonly int _k;
    private readonly TComparer _comparer;

    internal ArrayHeap(TopK<T, TComparer, ArrayHeap<T, TComparer>> topK)
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
                _content.Add(value);
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
                _content[0] = value;
                _content.AsSpan().Sort(_comparer);
                return;
            }
        }

        EnsureSorted();
        _content[0] = value;
        _content.AsSpan().Sort(_comparer);
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