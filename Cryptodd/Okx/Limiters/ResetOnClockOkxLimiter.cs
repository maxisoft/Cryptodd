namespace Cryptodd.Okx.Limiters;

internal sealed class ResetOnClockOkxLimiter : BaseResetOnScheduleOkxLimiter
{
    private long _periodMs = (long)DefaultPeriod.TotalMilliseconds;

    public override TimeSpan Period
    {
        get => TimeSpan.FromMilliseconds(_periodMs);
        set
        {
            var longValue = (long)value.TotalMilliseconds;
            if (longValue <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "negative");
            }

            _periodMs = longValue;
        }
    }

    protected override bool ShouldReset(DateTimeOffset now)
    {
        var ms = now.ToUnixTimeMilliseconds();
        var oldMs = DateTime.ToUnixTimeMilliseconds();
        var periodMs = _periodMs;
        return oldMs / periodMs != ms / periodMs;
    }
}