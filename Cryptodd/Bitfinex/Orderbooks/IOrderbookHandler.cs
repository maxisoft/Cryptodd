using Cryptodd.Bitfinex.Models;

namespace Cryptodd.Bitfinex.Orderbooks;

public readonly record struct OrderbookHandlerQuery(int Precision, int Length);

public interface IOrderbookHandler
{
    public bool Disabled { get; set; }
    public Task Handle(OrderbookHandlerQuery query, IReadOnlyCollection<OrderbookEnvelope> orderbooks, CancellationToken cancellationToken);
}