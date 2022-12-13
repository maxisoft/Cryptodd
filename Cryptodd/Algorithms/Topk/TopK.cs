using System.Collections;

namespace Cryptodd.Algorithms.Topk;

public abstract class TopK<T, TComparer, THeap> : IEnumerable<T> where TComparer : IComparer<T> where THeap : IHeap<T>
{
    public TopK(int k, TComparer comparer)
    {
        if (k <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(k), k, "k need to be positive integer");
        }

        K = k;
        Comparer = comparer;
    }

    public TComparer Comparer { get; }

    protected THeap Heap { get; init; }

    public int K { get; }

    public int Count => Heap.Count;

    public IEnumerator<T> GetEnumerator() => Heap.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

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

public class TopKArrayHeap<T, TComparer> : TopK<T, TComparer, ArrayHeap<T, TComparer>> where TComparer : IComparer<T>
{
    public TopKArrayHeap(int k, TComparer comparer) : base(k, comparer)
    {
        Heap = new ArrayHeap<T, TComparer>(this);
    }
}

public class TopKArrayHeap<T> : TopKArrayHeap<T, Comparer<T>>
{
    public TopKArrayHeap(int k) : base(k, Comparer<T>.Default) { }
    public TopKArrayHeap(int k, Comparer<T> comparer) : base(k, comparer) { }
}