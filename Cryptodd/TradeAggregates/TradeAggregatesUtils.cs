using System.Runtime.CompilerServices;
using Maxisoft.Utils.Algorithms;

namespace Cryptodd.TradeAggregates;

public static class TradeAggregatesUtils
{
    public static readonly ReadOnlyMemory<TimeSpan> DefaultTradingPeriods;

    static TradeAggregatesUtils()
    {
        var ts = new TimeSpan[]
        {
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(3),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(15),
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(45),
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(2),
            TimeSpan.FromHours(3),
            TimeSpan.FromHours(4),
            TimeSpan.FromDays(1),
            TimeSpan.FromDays(7),
            TimeSpan.FromDays(30),
        };
        Array.Sort(ts);
        DefaultTradingPeriods = ts;
    }
    
    public static TimeSpan PrevPeriod(TimeSpan ts, int shift = 0) => PrevPeriod(ts, shift, DefaultTradingPeriods.Span);

    public static TimeSpan PrevPeriod(TimeSpan ts, int shift, ReadOnlySpan<TimeSpan> timespans)
    {
        var index = timespans.BinarySearch(ts);
        if (index < 0)
        {
            index = ~index;
        }
        else
        {
            index--;
        }

        return timespans[(index - shift).Clamp(0, timespans.Length - 1)];
    }


    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static TimeSpan Clamp(TimeSpan value, TimeSpan minimum, TimeSpan maximum)
    {
        if (value > maximum)
        {
            value = maximum;
        }

        if (value < minimum)
        {
            value = minimum;
        }

        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DateTimeOffset Max(in DateTimeOffset left, in DateTimeOffset right) => left > right ? left : right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DateTimeOffset Min(in DateTimeOffset left, in DateTimeOffset right) => left < right ? left : right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;
}