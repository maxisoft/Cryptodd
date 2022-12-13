namespace Cryptodd.OrderBooks;

public interface IFloatSerializableConverter<TIn, out TOut> where TOut: IFloatSerializable
{
    public TOut Convert(in TIn priceSizePair);
}