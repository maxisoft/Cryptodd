using System.Diagnostics;

namespace Cryptodd.FileSystem.OpenedFileLimiter;

public class OpenedFileSource
{
    public OpenedFileSource(string fileName)
    {
        FileName = fileName;
    }

    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; }
    public FileAccess FileAccess { get; set; }
    public FileMode FileMode { get; set; }
    public DateTimeOffset? DateTime { get; set; } = DateTimeOffset.Now;

    public string StackTrace { get; set; } =
#if DEBUG
        EnhancedStackTrace.Current().ToString();
#else
    "";
#endif
    public string Source { get; set; } = "undef";
}