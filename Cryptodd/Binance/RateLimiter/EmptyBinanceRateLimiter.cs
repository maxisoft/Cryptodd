using Cryptodd.IoC;

namespace Cryptodd.Binance.RateLimiter;

public class EmptyBinanceRateLimiter : IInternalBinanceRateLimiter, INoAutoRegister
{
    public int UsedWeight { get; set; }

    public DateTimeOffset DateTimeOffset { get; set; } = DateTimeOffset.UnixEpoch;
    public BinanceRateLimiterOptions Options { get; } = new();
    public int AvailableWeight => checked((int)(MaxUsableWeight * AvailableWeightMultiplier - UsedWeight));
    public long MaxUsableWeight { get; set; } = int.MaxValue >> 1;

    public ValueTask<IApiCallRegistration> WaitForSlot(Uri uri, int weight, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IApiCallRegistration>(new EmptyApiCallRegistration { Uri = uri, Weight = weight });

    public void UpdateUsedWeightFromBinance(int weight, DateTimeOffset dateTimeOffset)
    {
        UsedWeight = weight;
        DateTimeOffset = dateTimeOffset;
    }

    public float AvailableWeightMultiplier { get; set; } = 1.0f;

    private class EmptyApiCallRegistration : IApiCallRegistration
    {
        public void Dispose() { }

        public required Uri Uri { get; init; }
        public int Weight { get; set; }
        public DateTimeOffset RegistrationDate { get; private set; }
        public bool Valid { get; set; }

        public void SetRegistrationDate()
        {
            RegistrationDate = DateTimeOffset.Now;
        }
    }
}