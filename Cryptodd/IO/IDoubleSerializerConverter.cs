namespace Cryptodd.IO;

public interface IDoubleSerializerConverter<TIn, out TOut> where TOut: IDoubleSerializable
{
    public TOut Convert(in TIn doubleSerializable);
}