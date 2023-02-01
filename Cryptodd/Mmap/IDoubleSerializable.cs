namespace Cryptodd.Mmap;

public interface IDoubleSerializable
{
    public int WriteTo(Span<double> buffer);
    
    public int ExpectedSize { get; }
}