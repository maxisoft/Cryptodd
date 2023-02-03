using Cryptodd.Mmap.Writer;
using Cryptodd.Okx.Orderbooks.Handlers;

namespace Cryptodd.Okx.Collectors.Options;

public class OkxOptionDataWriterOptions : DataWriterOptions
{
    public const string DefaultExchange = OkxOrderBookWriter.DefaultExchange;
    public const string DefaultKind = "option";
    public OkxOptionDataWriterOptions()
    {
        CoalesceExchange(DefaultExchange);
        Kind = DefaultKind;
    }
}