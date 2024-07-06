using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using JasperFx.Core.Reflection;
using Towel;
using Towel.DataStructures;

namespace Cryptodd.Algorithms.Topk;

public sealed class RedBlackTreeHeap<T, TComparer> : IHeap<T> where TComparer : IComparer<T>
{
    private readonly TComparer _comparer;
    private readonly int _k;

    private readonly RedBlackTreeLinked<T, InternalComparer> _tree;
    private T? _minValue;

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
        bool recheckMinValue;
        var insert = count < _k;
        if (!insert)
        {
            insert = _comparer.Compare(value, _minValue!) > 0;
            recheckMinValue = true;
        }
        else
        {
            recheckMinValue = count + 1 >= _k;
        }

        if (!insert)
        {
            return;
        }

        // ReSharper disable RedundantAssignment
        var (success, exception) = _tree.TryAdd(value);

        ThrowOnFail(success, exception, "unable to insert value");

        if (Count > _k)
        {
            (success, exception) = _tree.TryRemove(_minValue!);
            if (!success && Count > _k)
            {
                Debug.WriteLine(
                    $"{GetType().NameInCode()} Invalid {nameof(_tree.TryRemove)}() ! Count: {Count}, k: {_k}, Value: {_minValue}, ActualMin: {_tree.CurrentLeast}");
                (success, exception) = _tree.TryRemove(_tree.CurrentLeast);
                recheckMinValue = success;
            }
            // ReSharper restore RedundantAssignment

            ThrowOnFail(success, exception, "unable to remove value");
        }

        if (recheckMinValue)
        {
            _minValue = _tree.CurrentLeast;
        }

        return;

        [Conditional("DEBUG")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ThrowOnFail(bool success, Exception? exception, string message)
        {
            if (success)
            {
                return;
            }

            if (exception is not null)
            {
                throw new ArgumentException(message, nameof(value), exception);
            }

            throw new ArgumentException(message, nameof(value));
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

    private readonly struct InternalComparer(TComparer comparer) : IFunc<T, T, CompareResult>
    {
        public CompareResult Invoke(T arg1, T arg2)
        {
            return comparer.Compare(arg1, arg2) switch
            {
                0 => CompareResult.Equal,
                < 0 => CompareResult.Less,
                _ => CompareResult.Greater
            };
        }
    }
}