using Cryptodd.IoC;
using Cryptodd.Okx.Http;
using Cryptodd.Okx.Models;

namespace Cryptodd.Okx.Orderbooks;

// ReSharper disable once UnusedType.Global
public class OkxOrderbookInstrumentLister : IOkxOrderbookInstrumentLister, IService
{
    private readonly IOkxInstrumentIdsProvider _okxInstrumentIdsProvider;

    public OkxOrderbookInstrumentLister(IOkxInstrumentIdsProvider okxInstrumentIdsProvider)
    {
        _okxInstrumentIdsProvider = okxInstrumentIdsProvider;
    }

    private int _lastCapacity;

    public async Task<List<string>> ListInstruments(CancellationToken cancellationToken)
    {
        var tasks = new[]
        {
            _okxInstrumentIdsProvider.ListInstrumentIds(OkxInstrumentType.Spot, cancellationToken: cancellationToken),
            _okxInstrumentIdsProvider.ListInstrumentIds(OkxInstrumentType.Margin, cancellationToken: cancellationToken),
            _okxInstrumentIdsProvider.ListInstrumentIds(OkxInstrumentType.Swap, cancellationToken: cancellationToken),
            _okxInstrumentIdsProvider.ListInstrumentIds(OkxInstrumentType.Futures, cancellationToken: cancellationToken)
        };

        await Task.WhenAll(tasks).ConfigureAwait(false);
        HashSet<string> dejaVu = new(_lastCapacity);
        List<string> res = new();
        foreach (var task in tasks)
        {
            res.AddRange((task.IsCompleted ? task.Result : await task.ConfigureAwait(false)).Where(id => dejaVu.Add(id)));
            task.Dispose();
        }

        _lastCapacity = res.Capacity;
        return res;
    }
}