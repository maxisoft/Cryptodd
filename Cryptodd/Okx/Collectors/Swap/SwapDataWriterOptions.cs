using Cryptodd.Mmap.Writer;
using Cryptodd.Okx.Orderbooks.Handlers;

namespace Cryptodd.Okx.Collectors.Swap;

public class SwapDataWriterOptions : DataWriterOptions
{
    public const string DefaultExchange = OkxOrderBookWriter.DefaultExchange;
    public const string DefaultKind = "swap";
    public SwapDataWriterOptions()
    {
        CoalesceExchange(DefaultExchange);
        Kind = DefaultKind;
    }
}