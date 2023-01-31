using Cryptodd.IoC;
using Lamar;
using Serilog;

namespace Cryptodd.Okx.Collectors.Swap;

public class SwapDataCollector : IService
{
    private readonly IContainer _container;
    private readonly ILogger _logger;

    public SwapDataCollector(ILogger logger, IContainer container)
    {
        _logger = logger.ForContext(GetType());
        _container = container;
    }
}