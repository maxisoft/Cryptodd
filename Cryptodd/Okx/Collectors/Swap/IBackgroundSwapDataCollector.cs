namespace Cryptodd.Okx.Collectors.Swap;

public interface IBackgroundSwapDataCollector
{
    Task CollectLoop(CancellationToken cancellationToken);
}