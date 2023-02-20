using System.Text;
using Cryptodd.IO;
using Cryptodd.IO.FileSystem;
using Cryptodd.IO.FileSystem.OpenedFileLimiter;
using Maxisoft.Utils.Empties;
using Serilog;

namespace Cryptodd.OrderBooks.Writer;

internal class InternalOrderBookWriterHandler<T> : IOrderBookWriterHandler<T>, IDisposable, IAsyncDisposable
    where T : IFloatSerializable
{
    public readonly string Symbol;
    public readonly OrderBookWriterOptions Options;
    public readonly IPathResolver PathResolver;
    public readonly IDefaultOpenedFileLimiter FileLimiter;
    public readonly ILogger Logger;
    private FileStream? _obFileStream;
    private FileStream? _timeFileStream;
    private IDisposable _obFileLimiterDisposable = new EmptyDisposable();
    private IDisposable _timeFileLimiterDisposable = new EmptyDisposable();
    private long _completedCount;

    private string currentObFile = "";
    private long prevTimestamp = 0;

    public InternalOrderBookWriterHandler(string symbol, OrderBookWriterOptions options, IPathResolver pathResolver,
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

    public async Task FixFileMisalignment(long obSizeInBytes)
    {
        if (_obFileStream is not null || _timeFileStream is not null || string.IsNullOrEmpty(currentObFile))
        {
            return;
        }

        var stable = false;

        async ValueTask FixAlignment(string filePath, long? size, long alignment)
        {
            Logger.Error("file {File} has invalid alignment", filePath);
            await using var f = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            f.SetLength(size.GetValueOrDefault(f.Length) / alignment * alignment);
        }

        while (!stable)
        {
            stable = true;
            var timeFilePath = GetTimeFilePath();
            var obFilePath = GetObFilePath();

            long timeTimeLength = 0;
            try
            {
                timeTimeLength = new FileInfo(timeFilePath).Length;
            }
            catch (FileNotFoundException)
            {
                try
                {
                    File.Delete(obFilePath);
                }
                catch (Exception e) when (e is DirectoryNotFoundException or FileNotFoundException) { }
            }

            long obFileLength = 0;
            try
            {
                obFileLength = new FileInfo(obFilePath).Length;
            }
            catch (FileNotFoundException)
            {
                try
                {
                    File.Delete(timeFilePath);
                }
                catch (Exception e) when (e is DirectoryNotFoundException or FileNotFoundException) { }
            }

            const long timeAlignment = sizeof(long);
            if (timeTimeLength > 0 && timeTimeLength % timeAlignment != 0)
            {
                await FixAlignment(timeFilePath, null, timeAlignment);
                stable = false;
                continue;
            }

            if (obFileLength > 0 && obFileLength % obSizeInBytes != 0)
            {
                await FixAlignment(obFilePath, null, obSizeInBytes);
                stable = false;
                continue;
            }

            var timeCount = timeTimeLength / timeAlignment;
            var obCount = obFileLength / obSizeInBytes;
            if (timeCount > obCount)
            {
                await FixAlignment(timeFilePath, obCount * timeAlignment, timeAlignment);
                stable = false;
            }
            else if (timeCount < obCount)
            {
                await FixAlignment(obFilePath, timeCount * obSizeInBytes, obSizeInBytes);
                stable = false;
            }
        }
    }

    private async Task DoWriteAsync(ReadOnlyMemory<byte> values, long timestamp, int callRecursion,
        CancellationToken cancellationToken)
    {
        async ValueTask<(FileStream, IDisposable)> OpenAppend(string file, FileAccess fileAccess = FileAccess.Write,
            FileMode fileMode = FileMode.Append)
        {
            IDisposable disposable = new EmptyDisposable();
            try
            {
                OpenedFileLimiterUnregisterOnDispose? unregisterOnDispose;
                while (!FileLimiter.TryRegister(
                           new OpenedFileSource(file)
                           {
                               Source = nameof(InternalOrderBookWriterHandler<T>),
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

        if (_obFileStream is null)
        {
            await Close(cancellationToken).ConfigureAwait(false);
            await FixFileMisalignment(values.Length).ConfigureAwait(false);
            var obFile = Options.FormatFile(Symbol, $"{timestamp:D14}");
            if (obFile != currentObFile)
            {
                if (string.IsNullOrWhiteSpace(currentObFile) || 0 >= prevTimestamp ||
                    !(Math.Abs(prevTimestamp - timestamp) < TimeSpan.FromHours(2).TotalMilliseconds))
                {
                    currentObFile = obFile;
                    prevTimestamp = timestamp;
                    await FixFileMisalignment(values.Length).ConfigureAwait(false);
                }
                else
                {
                    Logger.Verbose("Reusing previous file {FileName}", currentObFile);
                }
            }
        }

        if (_timeFileStream is null)
        {
            var timeFile = GetTimeFilePath();
            (_timeFileStream, _timeFileLimiterDisposable) = await OpenAppend(timeFile).ConfigureAwait(false);
        }


        if (_obFileStream is null)
        {
            try
            {
                var obFile = GetObFilePath();
                (_obFileStream, _obFileLimiterDisposable) = await OpenAppend(obFile).ConfigureAwait(false);
            }
            catch
            {
                await Close(cancellationToken);
                throw;
            }
        }

        if (Options.MaxFileSize > 0 && _obFileStream.Length >= Options.MaxFileSize && callRecursion <= 1)
        {
            await Close(cancellationToken).ConfigureAwait(false);
            await FixFileMisalignment(values.Length).ConfigureAwait(false);
            var obFile = Options.FormatFile(Symbol, $"{timestamp:D14}");
            if (obFile != currentObFile)
            {
                currentObFile = obFile;
                prevTimestamp = timestamp;
                await DoWriteAsync(values, timestamp, callRecursion + 1, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        try
        {
            using var timeFileLock = AppendFileLockHelper.CreateLocked(_timeFileStream, sizeof(long));
            // do not dispose the BinaryWriter as it does unneeded flushes
            var bw = new BinaryWriter(_timeFileStream, Encoding.UTF8, leaveOpen: true);
            // ReSharper disable once RedundantCast
            bw.Write((long)timestamp);
        }
        catch
        {
            await Close(cancellationToken).ConfigureAwait(false);
            await FixFileMisalignment(values.Length).ConfigureAwait(false);
            throw;
        }


        try
        {
            using var obFileLock = AppendFileLockHelper.CreateLocked(_obFileStream, values.Span);
            await _obFileStream.WriteAsync(values, cancellationToken).ConfigureAwait(false);
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

    private string GetObFilePath()
    {
        var obFile = PathResolver.Resolve(currentObFile,
            new ResolveOption()
            {
                FileType = "binary orderbook",
                IntendedAction = FileIntendedAction.Write | FileIntendedAction.Create | FileIntendedAction.Append
            });
        return obFile;
    }

    private string GetTimeFilePath()
    {
        var timeFile = PathResolver.Resolve(currentObFile + ".time",
            new ResolveOption()
            {
                FileType = "binary orderbook time",
                IntendedAction = FileIntendedAction.Write | FileIntendedAction.Create | FileIntendedAction.Append
            });
        return timeFile;
    }

    public async ValueTask EndWrite(CancellationToken cancellationToken)
    {
        if (100 * FileLimiter.Count >= 80 * FileLimiter.Limit)
        {
            await Close(cancellationToken).ConfigureAwait(false);
        }

        if (_timeFileStream is not null)
        {
            await _timeFileStream.FlushAsync(cancellationToken);
        }

        if (_obFileStream is not null)
        {
            await _obFileStream.FlushAsync(cancellationToken);
        }

        _completedCount++;
    }

    public async ValueTask Close(CancellationToken cancellationToken)
    {
        if (_timeFileStream is not null)
        {
            try
            {
                await _timeFileStream.DisposeAsync().ConfigureAwait(false);
                _timeFileStream = null;
            }
            finally
            {
                _timeFileLimiterDisposable.Dispose();
            }
        }

        if (_obFileStream is not null)
        {
            try
            {
                await _obFileStream.DisposeAsync().ConfigureAwait(false);
                _obFileStream = null;
            }
            finally
            {
                _obFileLimiterDisposable.Dispose();
            }
        }
    }

    private void ReleaseUnmanagedResources()
    {
        _obFileStream?.Dispose();
        _timeFileStream?.Dispose();
        _timeFileStream = null;
        _obFileStream = null;
    }

    private void Dispose(bool disposing)
    {
        ReleaseUnmanagedResources();
        if (disposing)
        {
            _obFileLimiterDisposable.Dispose();
            _timeFileLimiterDisposable.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~InternalOrderBookWriterHandler()
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