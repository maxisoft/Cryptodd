using Cryptodd.Ftx.Models.DatabasePoco;

namespace Cryptodd.Ftx.Futures;

public interface IFuturesStatsHandler
{
    bool Disabled { get; set; }
    Task Handle(IReadOnlyCollection<FutureStats> futureStats, CancellationToken cancellationToken);
}