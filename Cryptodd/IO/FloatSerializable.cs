namespace Cryptodd.IO;

public interface IFloatSerializable
{
    public int WriteTo(Span<float> buffer);
    
    public int ExpectedSize { get; }
}