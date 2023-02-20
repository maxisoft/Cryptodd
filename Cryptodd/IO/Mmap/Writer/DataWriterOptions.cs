using Cryptodd.Pairs;
using SmartFormat;

namespace Cryptodd.IO.Mmap.Writer;

// ReSharper disable once ClassNeverInstantiated.Global
public class DataWriterOptions
{
    public bool Enabled { get; set; } = true;
    public int MaxParallelism { get; set; }
    private const string DefaultExchange = "none";
    public string FileFormat { get; set; } = "{Exchange}/{Kind}/{SymbolEscaped}/{FileName}.{Extension}";
    public string Exchange { get; set; } = DefaultExchange;

    public string Kind { get; set; } = "data";

    public string Extension { get; set; } = "mm";

    public long MaxFileSize { get; set; } = 16 << 20;

    public int BufferCount { get; set; } = 8;

    public int WaitForFileLimitTimeout { get; set; } = 500;

    public TimeSpan MaxTimeSpanDiff { get; set; } = TimeSpan.FromHours(24);
    public bool FlushOnEndWrite { get; set; } = true;

    public virtual string FormatFile(string symbol, string fileName)
    {
        var symbolEscaped = PairSanitizer.Sanitize(symbol);
        return Smart.Format(FileFormat,
            new { Exchange, Kind, Extension, FileName = fileName, SymbolEscaped = symbolEscaped });
    }

    public void CoalesceExchange(string exchange)
    {
        if (Exchange == DefaultExchange && !string.IsNullOrWhiteSpace(exchange))
        {
            Exchange = exchange;
        }
    }
}