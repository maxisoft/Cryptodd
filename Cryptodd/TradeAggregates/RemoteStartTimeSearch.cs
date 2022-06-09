namespace Cryptodd.TradeAggregates;


/// <summary>
/// Base class to search for a starting time point.
/// inner algorithm behavior is to http call api endpoints using a kind of time based binary search. 
/// </summary>
public abstract class RemoteStartTimeSearch
{
    public DateTimeOffset MinimumDate { get; set; } = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public DateTimeOffset MaximumDate { get; set; } = DateTimeOffset.Now;

    public int MaxApiCall { get; set; } = 128;

    /// <summary>
    /// Perform a api call to find real data's minimal time according to given minimalTime
    /// </summary>
    /// <param name="minimalTime"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>The minimal DateTimeOffset found for minimalTime or null otherwise</returns>
    public abstract ValueTask<DateTimeOffset?> ApiCall(DateTimeOffset minimalTime,
        CancellationToken cancellationToken = default);
    
    public TimeSpan Resolution { get; set; } = TimeSpan.Zero;

    public int MaxStuckCounter { get; set; } = 5;

    public async Task<DateTimeOffset> Search(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var minimalDate = MinimumDate;
        var maximumDate = MaximumDate;

        var originalMinimalDate = minimalDate;
        var endDate = maximumDate;

        var times = await ApiCall(minimalDate, cancellationToken).ConfigureAwait(false);
        if (times.HasValue)
        {
            return times.Value > minimalDate ? times.Value : minimalDate;
        }

        var safeMinimalDate = maximumDate;
        var unsafeMinimalDate = minimalDate;
        int i;
        var stuck = 0;
        var prev = (safeMinimalDate, unsafeMinimalDate);
        for (i = 0; i < MaxApiCall - 1; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (minimalDate >= maximumDate)
            {
                break;
            }

            if (i > 0 && (safeMinimalDate, unsafeMinimalDate) == prev)
            {
                stuck++;
                if (stuck >= MaxStuckCounter)
                {
                    break;
                }
            }
            
            prev = (safeMinimalDate, unsafeMinimalDate);

            var midPoint =
                DateTimeOffset.FromUnixTimeMilliseconds((minimalDate.ToUnixTimeMilliseconds() +
                                                        endDate.ToUnixTimeMilliseconds()) / 2);

            times = await ApiCall(midPoint, cancellationToken).ConfigureAwait(false);

            if (safeMinimalDate > times)
            {
                safeMinimalDate = times.Value;
            }

            if (!times.HasValue)
            {
                unsafeMinimalDate = midPoint;
            }

            if (!times.HasValue || times > endDate)
            {
                minimalDate = midPoint;
            }
            else
            {
                endDate = midPoint;
                minimalDate = unsafeMinimalDate;
            }
        }

        // backward sequential search

        minimalDate = safeMinimalDate;
        var resolution = Resolution;
        if (resolution == TimeSpan.Zero)
        {
            resolution = (maximumDate - minimalDate).Duration();
        }
        for (; i < MaxApiCall; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var endTime = minimalDate;
            minimalDate -= resolution;
            if (minimalDate <= originalMinimalDate)
            {
                return originalMinimalDate;
            }

            if (endTime <= minimalDate)
            {
                return minimalDate;
            }

            times = await ApiCall(minimalDate, cancellationToken).ConfigureAwait(false);
            if (!times.HasValue)
            {
                return endTime > originalMinimalDate ? endTime : originalMinimalDate;
            }
        }

        throw new TimeoutException("unable to complete search");
    }
}