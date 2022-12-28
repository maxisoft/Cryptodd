namespace Cryptodd.Algorithms.Topk;

public interface IHeap<T> : IReadOnlyCollection<T>
{
    public void Add(in T value);
    public int CopyTo(Span<T> span);
}