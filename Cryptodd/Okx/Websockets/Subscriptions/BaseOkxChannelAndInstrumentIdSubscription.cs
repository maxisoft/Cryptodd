namespace Cryptodd.Okx.Websockets.Subscriptions;

public abstract class BaseOkxChannelAndInstrumentIdSubscription : OkxSubscription, IEquatable<BaseOkxChannelAndInstrumentIdSubscription>
{
    public BaseOkxChannelAndInstrumentIdSubscription(string channel, string instrumentId) : base(channel)
    {
        _instId = instrumentId;
    }


    private readonly string _instId;

    // ReSharper disable once ConvertToAutoProperty
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

    protected static bool Equals<T>(T? left, T? right) where T : BaseOkxChannelAndInstrumentIdSubscription
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

    public override bool Equals(object? obj) => obj is BaseOkxChannelAndInstrumentIdSubscription ob && Equals<BaseOkxChannelAndInstrumentIdSubscription>(this, ob);

    public override bool Equals(OkxSubscription? other) => other is BaseOkxChannelAndInstrumentIdSubscription ob && Equals<BaseOkxChannelAndInstrumentIdSubscription>(this, ob);

    public bool Equals(BaseOkxChannelAndInstrumentIdSubscription? other) => Equals<BaseOkxChannelAndInstrumentIdSubscription>(this, other);

    public override int GetHashCode() => HashCode.Combine(Channel, InstrumentId);

    #endregion
}