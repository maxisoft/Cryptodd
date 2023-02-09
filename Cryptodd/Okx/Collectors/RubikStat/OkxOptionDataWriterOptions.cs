using Cryptodd.Mmap.Writer;
using Cryptodd.Okx.Orderbooks.Handlers;

namespace Cryptodd.Okx.Collectors.Options;

public class OkxRubikDataWriterOptions : DataWriterOptions
{
    public const string DefaultExchange = OkxOrderBookWriter.DefaultExchange;
    public const string DefaultKind = "rubik";

    public OkxRubikDataWriterOptions()
    {
        CoalesceExchange(DefaultExchange);
        Kind = DefaultKind;
    }
}