using System.Runtime.InteropServices;

namespace Cryptodd.FileSystem;

public struct AppendFileLockHelper : IDisposable
{
    private readonly FileStream _fileStream;
    private readonly long _size;
    private bool _locked;
    private long _position;

    internal AppendFileLockHelper(FileStream fileStream, long size)
    {
        _fileStream = fileStream;
        _size = size;
        _locked = false;
        _position = 0;
    }

    public static AppendFileLockHelper CreateLocked(FileStream fileStream, ReadOnlySpan<byte> data) =>
        CreateLocked(fileStream, data.Length);

    public static AppendFileLockHelper CreateLocked<T>(FileStream fileStream, ReadOnlySpan<T> data) where T : struct =>
        CreateLocked(fileStream, MemoryMarshal.Cast<T, byte>(data));

    public static AppendFileLockHelper CreateLocked(FileStream fileStream, long size)
    {
        var res = new AppendFileLockHelper(fileStream, size);
        try
        {
            res.Lock();
            return res;
        }
        catch
        {
            res.Dispose();
            throw;
        }
    }

    public void Lock()
    {
        if (_locked)
        {
            throw new Exception("Trying to lock 2x");
        }

        _position = _fileStream.Position;
        if (_size <= 0)
        {
            return;
        }

        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())
        {
            return;
        }

        _locked = true;
        try
        {
            _fileStream.Lock(_position, _size);
        }
        catch
        {
            _locked = false;
            throw;
        }
    }

    public void Unlock()
    {
        if (!_locked)
        {
            throw new Exception("Trying to unlock a non locked file");
        }

        if (_size <= 0)
        {
            return;
        }

        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())
        {
            return;
        }

        _locked = false;
        try
        {
            _fileStream.Unlock(_position, _size);
        }
        catch
        {
            _locked = true;
            throw;
        }
    }

    public void Dispose()
    {
        if (_locked)
        {
            Unlock();
        }
    }
}