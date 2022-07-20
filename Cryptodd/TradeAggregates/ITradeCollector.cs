using Cryptodd.IoC;

namespace Cryptodd.TradeAggregates;

public interface ITradeCollector : IService
{
    Task Collect(CancellationToken cancellationToken);
}