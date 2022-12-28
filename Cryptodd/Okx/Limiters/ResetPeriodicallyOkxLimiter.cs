namespace Cryptodd.Okx.Limiters;

internal sealed class ResetPeriodicallyOkxLimiter : BaseResetOnScheduleOkxLimiter
{
    private TimeSpan _period = DefaultPeriod;

    // ReSharper disable once ConvertToAutoPropertyWhenPossible
    public override TimeSpan Period
    {
        get => _period;
        set
        {
            if ((long)value.TotalMilliseconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "negative");
            }

            _period = value;
        }
    }

    protected override bool ShouldReset(DateTimeOffset now) => (now - DateTime).Duration() > _period;
}