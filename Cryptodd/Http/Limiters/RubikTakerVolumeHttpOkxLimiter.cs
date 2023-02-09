namespace Cryptodd.Http.Limiters;

public class RubikTakerVolumeHttpOkxLimiter : Common5HttpOkxLimiter
{
    public RubikTakerVolumeHttpOkxLimiter() : base(DefaultPeriod, DefaultMaxLimit - 2)
    {
        
    }
}