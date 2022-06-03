using Cryptodd.Ftx.Models;

namespace Cryptodd.Ftx;

public interface IGroupedOrderbookHandler
{
    public bool Disabled { get; set; }
    public Task Handle(IReadOnlyCollection<GroupedOrderbookDetails> orderbooks, CancellationToken cancellationToken);
}