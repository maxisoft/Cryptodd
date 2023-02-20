using Maxisoft.Utils.Collections.Lists;

namespace Cryptodd.Bitfinex;

public interface IBitfinexPairProvider
{
    Task<ArrayList<string>> GetAllPairs(CancellationToken cancellationToken);
}