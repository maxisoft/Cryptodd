using System.Collections;

namespace Cryptodd.Algorithms.Topk;

public abstract class TopK<T, TComparer, THeap>: IEnumerable<T> where TComparer : IComparer<T> where THeap : IHeap<T>
{
    private readonly TComparer _comparer;
    public TComparer Comparer => _comparer;
    private readonly int _k;
    protected THeap Heap { get; init; }

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