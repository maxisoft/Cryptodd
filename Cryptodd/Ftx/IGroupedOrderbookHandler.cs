using Cryptodd.Ftx.Models;

namespace Cryptodd.Ftx;

public interface IGroupedOrderbookHandler
{
    public Task Handle(IReadOnlyCollection<GroupedOrderbookDetails> orderbooks, CancellationToken cancellationToken);
    
    public bool Disabled { get; set; } 
}