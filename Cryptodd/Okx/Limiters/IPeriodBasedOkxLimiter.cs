namespace Cryptodd.Okx.Limiters;

public interface IPeriodBasedOkxLimiter : IOkxLimiter
{
    TimeSpan Period { get; set; }
}