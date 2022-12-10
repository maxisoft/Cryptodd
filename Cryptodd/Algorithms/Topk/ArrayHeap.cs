using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Maxisoft.Utils.Collections.Lists;

namespace Cryptodd.Algorithms.Topk;

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