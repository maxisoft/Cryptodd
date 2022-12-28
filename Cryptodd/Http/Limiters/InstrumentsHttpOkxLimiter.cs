namespace Cryptodd.Http;

public class InstrumentsHttpOkxLimiter : HttpOkxLimiter
{
    public const int DefaultMaxLimit = 20;
    public static readonly TimeSpan DefaultPeriod = TimeSpan.FromSeconds(2.2);
    private InstrumentsHttpOkxLimiter(TimeSpan period, int maxLimit) : base(period, maxLimit) { }

    public InstrumentsHttpOkxLimiter() : this(DefaultPeriod, DefaultMaxLimit) { }
}