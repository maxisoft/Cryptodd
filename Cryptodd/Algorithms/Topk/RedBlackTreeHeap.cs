using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Towel;
using Towel.DataStructures;

namespace Cryptodd.Algorithms.Topk;

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