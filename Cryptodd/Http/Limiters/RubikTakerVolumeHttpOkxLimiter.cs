namespace Cryptodd.Http.Limiters;

public class RubikTakerVolumeHttpOkxLimiter : Common5HttpOkxLimiter
{
    public new static readonly TimeSpan DefaultPeriod = TimeSpan.FromSeconds(5.2);
    public new const int DefaultMaxLimit = 2;
    public RubikTakerVolumeHttpOkxLimiter() : base(DefaultPeriod, DefaultMaxLimit)
    {
        
    }
}