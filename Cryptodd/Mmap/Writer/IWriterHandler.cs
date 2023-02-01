using Cryptodd.OrderBooks;

namespace Cryptodd.Mmap.Writer;

public interface IWriterHandler<T> where T : IDoubleSerializable
{
    public ValueTask BeginWrite(int countHint = -1, CancellationToken cancellationToken = default);
    public Task WriteAsync(ReadOnlyMemory<byte> values, long timestamp, CancellationToken cancellationToken);

    public ValueTask EndWrite(CancellationToken cancellationToken);
}