using Cryptodd.Pairs;
using SmartFormat;

namespace Cryptodd.OrderBooks.Writer;

public class OrderBookWriterOptions
{
    public bool Enabled { get; set; } = true;
    public int MaxParallelism { get; set; }
    private const string DefaultExchange = "none";
    public string FileFormat { get; set; } = "{Exchange}/ob/{SymbolEscaped}/{FileName}.{Extension}";
    public string Exchange { get; set; } = DefaultExchange;
    public string Extension { get; set; } = "mm";

    public long MaxFileSize { get; set; } = 16 << 20;

    public int BookMemoryBufferCount { get; set; } = 8;

    public virtual string FormatFile(string symbol, string fileName)
    {
        var symbolEscaped = PairSanitizer.Sanitize(symbol);
        return Smart.Format(FileFormat,
            new { Exchange, Extension, FileName = fileName, SymbolEscaped = symbolEscaped });
    }

    public void CoalesceExchange(string exchange)
    {
        if (Exchange == DefaultExchange && !string.IsNullOrWhiteSpace(exchange))
        {
            Exchange = exchange;
        }
    }
}