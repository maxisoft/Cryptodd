using Cryptodd.Ftx.Models;

namespace Cryptodd.Ftx.Orderbooks;

public interface IGroupedOrderbookHandler
{
    public bool Disabled { get; set; }
    public Task Handle(IReadOnlyCollection<GroupedOrderbookDetails> orderbooks, CancellationToken cancellationToken);
}