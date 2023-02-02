namespace Cryptodd.Http.Limiters;

public abstract class Common20HttpOkxLimiter : HttpOkxLimiter
{
    public const int DefaultMaxLimit = 20;
    public static readonly TimeSpan DefaultPeriod = TimeSpan.FromSeconds(2.2);
    private Common20HttpOkxLimiter(TimeSpan period, int maxLimit) : base(period, maxLimit) { }

    public Common20HttpOkxLimiter() : this(DefaultPeriod, DefaultMaxLimit) { }
}