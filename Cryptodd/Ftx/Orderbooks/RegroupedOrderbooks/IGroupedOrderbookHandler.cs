namespace Cryptodd.Ftx.Orderbooks.RegroupedOrderbooks;

public interface IRegroupedOrderbookHandler
{
    public bool Disabled { get; set; }
    public Task Handle(IReadOnlyCollection<RegroupedOrderbook> orderbooks, CancellationToken cancellationToken);
}