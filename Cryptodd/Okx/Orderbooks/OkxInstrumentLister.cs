using Cryptodd.IoC;
using Cryptodd.Okx.Http;
using Cryptodd.Okx.Models;
using Maxisoft.Utils.Collections.Dictionaries;
using Maxisoft.Utils.Empties;

namespace Cryptodd.Okx.Orderbooks;

// ReSharper disable once UnusedType.Global
public class OkxOrderbookInstrumentLister(IOkxInstrumentIdsProvider okxInstrumentIdsProvider)
    : IOkxOrderbookInstrumentLister, IService
{
    private int _lastCapacity;

    public async Task<ICollection<string>> ListInstruments(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tasks = new[]
        {
            okxInstrumentIdsProvider.ListInstrumentIds(OkxInstrumentType.Spot, cancellationToken: cancellationToken),
            okxInstrumentIdsProvider.ListInstrumentIds(OkxInstrumentType.Margin, cancellationToken: cancellationToken),
            okxInstrumentIdsProvider.ListInstrumentIds(OkxInstrumentType.Swap, cancellationToken: cancellationToken),
            okxInstrumentIdsProvider.ListInstrumentIds(OkxInstrumentType.Futures, cancellationToken: cancellationToken)
        };

        await Task.WhenAll(tasks).ConfigureAwait(false);
        OrderedDictionary<string, EmptyStruct> res = new(_lastCapacity);
        foreach (var task in tasks)
        {
            try
            {
                foreach (var instrument in task.IsCompletedSuccessfully
                             ? task.Result
                             : await task.ConfigureAwait(false))
                {
                    res.TryAdd(instrument, default);
                }
            }
            finally
            {
                task.Dispose();
            }
        }

        _lastCapacity = res.Count;
        return res.Keys;
    }
}