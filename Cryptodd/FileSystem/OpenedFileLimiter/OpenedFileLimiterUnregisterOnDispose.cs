namespace Cryptodd.FileSystem.OpenedFileLimiter;

public sealed class OpenedFileLimiterUnregisterOnDispose : IDisposable
{
    private readonly OpenedFileLimiter _limiter;
    private LinkedListNode<OpenedFileSource>? _source;

    internal OpenedFileLimiterUnregisterOnDispose(OpenedFileLimiter limiter,
        LinkedListNode<OpenedFileSource>? source)
    {
        _limiter = limiter;
        _source = source;
    }

    public void Dispose()
    {
        _limiter.Remove(ref _source);
        _source = null;
    }
}