using Cryptodd.Bitfinex.Models;

namespace Cryptodd.Bitfinex.Orderbooks;

public interface IOrderbookHandler
{
    public bool Disabled { get; set; }
    public Task Handle(IReadOnlyCollection<OrderbookEnvelope> orderbooks, CancellationToken cancellationToken);
}