namespace Cryptodd.Okx.Websockets.Subscriptions;

public sealed class OkxFundingRateSubscription : BaseOkxChannelAndInstrumentIdSubscription, IEquatable<OkxFundingRateSubscription>
{
    public const string DefaultChannel = "funding-rate";
    
    public OkxFundingRateSubscription(string channel, string instrumentId) : base(channel, instrumentId)
    {
    }
    
    public OkxFundingRateSubscription(string instrumentId) : this(DefaultChannel, instrumentId)
    {
    }

    public bool Equals(OkxFundingRateSubscription? other) => Equals<OkxFundingRateSubscription>(this, other);
}