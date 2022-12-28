using System.Buffers;
using System.Text.Unicode;

namespace Cryptodd.Okx.Websockets.Subscriptions;

public abstract class OkxSubscription : IEquatable<OkxSubscription>
{
    protected readonly string _channel;

    public string Channel => _channel;

    protected OkxSubscription(string channel)
    {
        _channel = channel;
    }

    protected abstract Span<byte> WriteSubscribeArgs(Span<byte> buffer);
    
    public virtual Span<byte> WriteSubscribePayload(Span<byte> buffer) => WriteSubscribeArgs(buffer);
    public virtual Span<byte> WriteUnsubscribePayload(Span<byte> buffer) => WriteSubscribeArgs(buffer);

    public abstract bool Equals(OkxSubscription? other);

    protected internal static int WriteString(Span<byte> buffer, string s, char escape = '"')
    {
        if (escape != default && s.Contains(escape))
        {
            s = s.Replace("\"", "\\\"", StringComparison.InvariantCulture);
        }

        return WriteString(buffer, (ReadOnlySpan<char>)s, default);
    }

    protected internal static int WriteString(Span<byte> buffer, ReadOnlySpan<char> s, char escape = '"')
    {
        if (escape != default && s.IndexOf(escape) >= 0)
        {
            throw new NotImplementedException("escaping not impl");
        }

        if (Utf8.FromUtf16(s, buffer, out _, out var bytesWritten) == OperationStatus.Done)
        {
            return bytesWritten;
        }

        return -1;
    }

    protected internal static int WriteBytes(Span<byte> buffer, ReadOnlySpan<byte> b)
    {
        if (b.Length <= buffer.Length)
        {
            b.CopyTo(buffer);
            return b.Length;
        }

        return -1;
    }

    public override bool Equals(object? obj) => obj is OkxSubscription subscription && Equals(subscription);

    public override int GetHashCode() => Channel.GetHashCode();
}