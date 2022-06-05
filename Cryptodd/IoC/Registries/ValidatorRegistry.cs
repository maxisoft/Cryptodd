using Cryptodd.Ftx.Models;
using Cryptodd.Ftx.Orderbooks;
using Lamar;

namespace Cryptodd.IoC.Registries;

public class ValidatorRegistry : ServiceRegistry
{
    public ValidatorRegistry()
    {
        For<AsciiStringValidator>().UseIfNone<AsciiStringValidator>();
        For<IValidator<GroupedOrderbookDetails>>().Add<FtxOrderbookValidator>();
    }
}