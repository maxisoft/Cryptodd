namespace Cryptodd.Okx.Websockets.Subscriptions;

public sealed class OkxOrderbookSubscription : BaseOkxChannelAndInstrumentIdSubscription, IEquatable<OkxOrderbookSubscription>
{
    public const string DefaultBookChannel = "books";
    
    public OkxOrderbookSubscription(string channel, string instrumentId) : base(channel, instrumentId)
    {
    }
    
    public OkxOrderbookSubscription(string instrumentId) : this(DefaultBookChannel, instrumentId)
    {
    }

    public bool Equals(OkxOrderbookSubscription? other) => Equals<OkxOrderbookSubscription>(this, other);
}