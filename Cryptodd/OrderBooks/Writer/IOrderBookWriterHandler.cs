using Cryptodd.IO;

namespace Cryptodd.OrderBooks.Writer;

public interface IOrderBookWriterHandler<T> where T : IFloatSerializable
{
    public ValueTask BeginWrite(int countHint = -1, CancellationToken cancellationToken = default);
    public Task WriteAsync(ReadOnlyMemory<byte> values, long timestamp, CancellationToken cancellationToken);

    public ValueTask EndWrite(CancellationToken cancellationToken);
}