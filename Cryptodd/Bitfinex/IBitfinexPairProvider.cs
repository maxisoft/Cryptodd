using Maxisoft.Utils.Collections.Lists;

namespace Cryptodd.Bitfinex;

public interface IBitfinexPairProvider
{
    ValueTask<ArrayList<string>> GetAllPairs(CancellationToken cancellationToken);
}