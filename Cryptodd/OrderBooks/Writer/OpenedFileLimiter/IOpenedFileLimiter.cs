namespace Cryptodd.OrderBooks.Writer.OpenedFileLimiter;

public interface IOpenedFileLimiter
{
    public int Count { get; }
    Task Wait(int fileLimit, CancellationToken cancellationToken);
    public bool TryRegister(OpenedFileSource fileSource, int fileLimit, out OpenedFileLimiterUnregisterOnDispose res);
}