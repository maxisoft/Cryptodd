namespace Cryptodd.Algorithms.Topk;

public interface IHeap<T>: IEnumerable<T>
{
    public int Count { get; }
    public void Add(in T value);
    public int CopyTo(Span<T> span);
}