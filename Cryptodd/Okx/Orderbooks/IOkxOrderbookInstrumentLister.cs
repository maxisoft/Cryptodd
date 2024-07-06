namespace Cryptodd.Okx.Orderbooks;

public interface IOkxOrderbookInstrumentLister
{
    public Task<ICollection<string>> ListInstruments(CancellationToken cancellationToken);
}