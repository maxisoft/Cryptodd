namespace Cryptodd.Okx.Websockets.Subscriptions;

public class OkxOrderbookSubscription : OkxSubscription, IEquatable<OkxOrderbookSubscription>
{
    public const string DefaultBookChannel = "books";
    
    public OkxOrderbookSubscription(string channel, string instrumentId) : base(channel)
    {
        _instId = instrumentId;
    }
    
    public OkxOrderbookSubscription(string instrumentId) : this(DefaultBookChannel, instrumentId)
    {
        _instId = instrumentId;
    }


    private readonly string _instId;

    public string InstrumentId => _instId;

    #region WriteSubscribeArgs

    protected override Span<byte> WriteSubscribeArgs(Span<byte> buffer)
    {
        var c = 0;

        {
            var writeBytes = WriteBytes(buffer, "{\"channel\":\""u8);
            if (writeBytes < 0)
            {
                return Span<byte>.Empty;
            }

            c += writeBytes;
        }

        {
            var writeBytes = WriteString(buffer[c..], Channel);
            if (writeBytes < 0)
            {
                return Span<byte>.Empty;
            }

            c += writeBytes;
        }


        {
            var writeBytes = WriteBytes(buffer[c..], "\",\"instId\":\""u8);
            if (writeBytes < 0)
            {
                return Span<byte>.Empty;
            }

            c += writeBytes;
        }

        {
            var writeBytes = WriteString(buffer[c..], InstrumentId);
            if (writeBytes < 0)
            {
                return Span<byte>.Empty;
            }

            c += writeBytes;
        }

        {
            var writeBytes = WriteBytes(buffer[c..], "\"}"u8);
            if (writeBytes < 0)
            {
                return Span<byte>.Empty;
            }

            c += writeBytes;
        }

        return buffer[..c];
    }

    #endregion


    #region Equality

    protected static bool Equals<T>(T? left, T? right) where T : OkxOrderbookSubscription
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (right is null || left is null)
        {
            return false;
        }

        if (left.GetType() != right.GetType())
        {
            return false;
        }

        return left.Channel == right.Channel && left.InstrumentId == right.InstrumentId;
    }

    public override bool Equals(object? obj) => obj is OkxOrderbookSubscription ob && Equals<OkxOrderbookSubscription>(this, ob);

    public override bool Equals(OkxSubscription? other) => other is OkxOrderbookSubscription ob && Equals<OkxOrderbookSubscription>(this, ob);

    public bool Equals(OkxOrderbookSubscription? other) => Equals<OkxOrderbookSubscription>(this, other);

    public override int GetHashCode() => HashCode.Combine(Channel, InstrumentId);

    #endregion
}