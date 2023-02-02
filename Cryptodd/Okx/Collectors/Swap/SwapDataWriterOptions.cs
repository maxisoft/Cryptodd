using Cryptodd.Mmap.Writer;

namespace Cryptodd.Okx.Collectors.Swap;

public class SwapDataWriterOptions : DataWriterOptions
{
    public SwapDataWriterOptions()
    {
        CoalesceExchange("okx");
        Kind = "swap";
    }
}