namespace Cryptodd.Mmap;

public interface IDoubleSerializerConverter<TIn, out TOut> where TOut: IDoubleSerializable
{
    public TOut Convert(in TIn doubleSerializable);
}