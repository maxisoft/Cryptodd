namespace Cryptodd.Okx.Orderbooks;

public interface IOkxOrderbookInstrumentLister
{
    public Task<List<string>> ListInstruments(CancellationToken cancellationToken);
}