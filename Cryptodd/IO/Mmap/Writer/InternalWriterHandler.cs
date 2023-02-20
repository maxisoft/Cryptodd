using Cryptodd.IO.FileSystem;
using Cryptodd.IO.FileSystem.OpenedFileLimiter;
using Maxisoft.Utils.Empties;
using Serilog;

namespace Cryptodd.IO.Mmap.Writer;

internal class InternalWriterHandler<T> : IWriterHandler<T>, IDisposable, IAsyncDisposable
    where T : IDoubleSerializable
{
    public readonly string Symbol;
    public readonly DataWriterOptions Options;
    public readonly IPathResolver PathResolver;
    public readonly IDefaultOpenedFileLimiter FileLimiter;
    public readonly ILogger Logger;
    private FileStream? _fileStream;
    private IDisposable _fileLimiterDisposable = new EmptyDisposable();
    private long _completedCount;

    private string _currentFile = "";
    private long _prevTimestamp = 0;

    public InternalWriterHandler(string symbol, DataWriterOptions options, IPathResolver pathResolver,
        ILogger logger, IDefaultOpenedFileLimiter fileLimiter)
    {
        Symbol = symbol;
        Options = options;
        PathResolver = pathResolver;
        FileLimiter = fileLimiter;
        Logger = logger.ForContext(GetType());
    }

    public async ValueTask BeginWrite(int countHint = -1, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(Options.WaitForFileLimitTimeout);
        try
        {
            await FileLimiter.Wait(cts.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is OperationCanceledException or OperationCanceledException)
        {
            if (!cts.IsCancellationRequested)
            {
                throw;
            }

            Logger.Warning(e, "File system seems too slow to handle order book writes");
        }
    }

    public async Task FixFileMisalignment(long sizeInBytes)
    {
        if (_fileStream is not null || string.IsNullOrEmpty(_currentFile))
        {
            return;
        }

        async ValueTask FixAlignment(string filePath, long? size, long alignment)
        {
            Logger.Error("file {File} has invalid alignment", filePath);
            await using var f = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            using var fileLock = AppendFileLockHelper.CreateLocked(f, size.GetValueOrDefault(f.Length));
            f.SetLength(size.GetValueOrDefault(f.Length) / alignment * alignment);
        }


        var filePath = GetFilePath();

        long fileLength;
        try
        {
            fileLength = new FileInfo(filePath).Length;
        }
        catch (FileNotFoundException)
        {
            return;
        }

        if (fileLength > 0 && fileLength % sizeInBytes != 0)
        {
            await FixAlignment(filePath, null, sizeInBytes);
        }
    }

    private async Task DoWriteAsync(ReadOnlyMemory<byte> values, long timestamp, int callRecursion,
        CancellationToken cancellationToken)
    {
        async ValueTask<(FileStream, IDisposable)> OpenAppend(string file, FileAccess fileAccess = FileAccess.Write,
            FileMode fileMode = FileMode.Append)
        {
            IDisposable? disposable = new EmptyDisposable();
            try
            {
                OpenedFileLimiterUnregisterOnDispose? unregisterOnDispose;
                while (!FileLimiter.TryRegister(
                           new OpenedFileSource(file)
                           {
                               Source = nameof(InternalWriterHandler<T>),
                               FileMode = fileMode,
                               FileAccess = fileAccess
                           }, out unregisterOnDispose))
                {
                    await FileLimiter.Wait(cancellationToken).ConfigureAwait(false);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                }

                disposable = unregisterOnDispose ?? disposable;


                try
                {
                    return (File.Open(file, fileMode, fileAccess, FileShare.ReadWrite), disposable);
                }
                catch (DirectoryNotFoundException)
                {
                    var dir = Path.GetDirectoryName(file);
                    if (dir is not null && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    else
                    {
                        throw;
                    }

                    return (File.Open(file, fileMode, fileAccess, FileShare.ReadWrite), disposable);
                }
            }
            catch
            {
                disposable?.Dispose();
                throw;
            }
        }

        if (_fileStream is null)
        {
            await Close(cancellationToken).ConfigureAwait(false);
            await FixFileMisalignment(values.Length).ConfigureAwait(false);
            var file = Options.FormatFile(Symbol, $"{timestamp:D14}");
            if (file != _currentFile)
            {
                if (string.IsNullOrWhiteSpace(_currentFile) || 0 >= _prevTimestamp ||
                    !(Math.Abs(_prevTimestamp - timestamp) < Options.MaxTimeSpanDiff.TotalMilliseconds))
                {
                    _currentFile = file;
                    _prevTimestamp = timestamp;
                    await FixFileMisalignment(values.Length).ConfigureAwait(false);
                }
                else
                {
                    Logger.Verbose("Reusing previous file {FileName}", _currentFile);
                }
            }
        }


        if (_fileStream is null)
        {
            try
            {
                var file = GetFilePath();
                (_fileStream, _fileLimiterDisposable) = await OpenAppend(file).ConfigureAwait(false);
            }
            catch
            {
                await Close(cancellationToken);
                throw;
            }
        }

        if (Options.MaxFileSize > 0 && _fileStream.Length >= Options.MaxFileSize && callRecursion <= 1)
        {
            await Close(cancellationToken).ConfigureAwait(false);
            await FixFileMisalignment(values.Length).ConfigureAwait(false);
            var obFile = Options.FormatFile(Symbol, $"{timestamp:D14}");
            if (obFile != _currentFile)
            {
                _currentFile = obFile;
                _prevTimestamp = timestamp;
                await DoWriteAsync(values, timestamp, callRecursion + 1, cancellationToken).ConfigureAwait(false);
                return;
            }
        }


        try
        {
            using var obFileLock = AppendFileLockHelper.CreateLocked(_fileStream, values.Span);
            await _fileStream.WriteAsync(values, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await Close(cancellationToken).ConfigureAwait(false);
            await FixFileMisalignment(values.Length).ConfigureAwait(false);
            throw;
        }
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> values, long timestamp, CancellationToken cancellationToken)
    {
        await DoWriteAsync(values, timestamp, 0, cancellationToken);
    }

    private string GetFilePath()
    {
        var obFile = PathResolver.Resolve(_currentFile,
            new ResolveOption()
            {
                FileType = "binary mmap",
                IntendedAction = FileIntendedAction.Write | FileIntendedAction.Create | FileIntendedAction.Append
            });
        return obFile;
    }

    public async ValueTask EndWrite(CancellationToken cancellationToken)
    {
        if (100 * FileLimiter.Count >= 80 * FileLimiter.Limit)
        {
            await Close(cancellationToken).ConfigureAwait(false);
        }

        if (_fileStream is not null && Options.FlushOnEndWrite)
        {
            await _fileStream.FlushAsync(cancellationToken);
        }

        _completedCount++;
    }

    public async ValueTask Close(CancellationToken cancellationToken)
    {
        if (_fileStream is not null)
        {
            try
            {
                await _fileStream.DisposeAsync().ConfigureAwait(false);
                _fileStream = null;
            }
            finally
            {
                _fileLimiterDisposable.Dispose();
            }
        }
    }

    private void ReleaseUnmanagedResources()
    {
        _fileStream?.Dispose();
        _fileStream = null;
    }

    private void Dispose(bool disposing)
    {
        ReleaseUnmanagedResources();
        if (disposing)
        {
            _fileLimiterDisposable.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~InternalWriterHandler()
    {
        Dispose(false);
    }

    public async ValueTask DisposeAsync()
    {
        await Close(CancellationToken.None);
        ReleaseUnmanagedResources();
        GC.SuppressFinalize(this);
    }
}