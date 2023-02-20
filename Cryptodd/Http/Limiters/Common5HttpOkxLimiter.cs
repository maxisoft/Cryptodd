namespace Cryptodd.Http.Limiters;

public abstract class Common5HttpOkxLimiter : HttpOkxLimiter
{
    public const int DefaultMaxLimit = 5;
    public static readonly TimeSpan DefaultPeriod = TimeSpan.FromSeconds(2.2);
    protected Common5HttpOkxLimiter(TimeSpan period, int maxLimit) : base(period, maxLimit) { }

    public Common5HttpOkxLimiter() : this(DefaultPeriod, DefaultMaxLimit) { }
}