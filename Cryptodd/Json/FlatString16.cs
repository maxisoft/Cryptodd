using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Unicode;

namespace Cryptodd.Json;

[StructLayout(LayoutKind.Sequential)]
public struct FlatString16 : IEquatable<FlatString16>, IComparable<FlatString16>, IComparable
{
    private byte length;
    private byte c0;
    private byte c2;
    private byte c3;
    private byte c4;
    private byte c5;
    private byte c6;
    private byte c7;
    private byte c8;
    private byte c9;
    private byte c10;
    private byte c12;
    private byte c13;
    private byte c14;
    private byte c15;

    public int Length
    {
        get => length;
        set => SetLength(value);
    }

    public const int MaxLength = 16;

    internal void SetLength(int length)
    {
        if (length > MaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, $"length must be lower than {MaxLength}");
        }

        this.length = checked((byte)length);
        if (length < MaxLength)
        {
            AsSpan(ref this, MaxLength)[length] = 0;
        }
    }

    public static Span<byte> AsSpan(ref FlatString16 s16, int length)
    {
        unsafe
        {
            var ptr = (byte*)Unsafe.AsPointer(ref s16.c0);
            Debug.Assert(length <= MaxLength);
            return new Span<byte>(ptr, length);
        }
    }

    public static Span<byte> AsSpan(ref FlatString16 s16) => AsSpan(ref s16, s16.length);

    public void CopyTo(ref FlatString16 s16)
    {
        AsSpan(ref this).CopyTo(AsSpan(ref s16, MaxLength));
        s16.SetLength(Length);
    }

    public int CopyTo(Span<byte> span)
    {
        AsSpan(ref this).CopyTo(span);
        return Length;
    }

    public static implicit operator string(FlatString16 s16) => s16.ToString();

    public static implicit operator FlatString16(string s) => (ReadOnlySpan<char>)s;

    public static implicit operator FlatString16(ReadOnlySpan<char> s)
    {
        var res = new FlatString16();
        var result = Utf8.FromUtf16(s, AsSpan(ref res, MaxLength), out var charsRead, out var bytesWritten);
        if (result != OperationStatus.Done || charsRead != s.Length)
        {
            throw new ArgumentException("Unable to convert to string16", nameof(s));
        }

        res.SetLength(bytesWritten);
        return res;
    }

    public static implicit operator FlatString16(ReadOnlySpan<byte> s)
    {
        var res = new FlatString16();
        res.SetLength(s.Length);
        s.CopyTo(AsSpan(ref res));
        return res;
    }

    # region CompareTo

    public int CompareTo(FlatString16 other) => CompareTo(ref other);
    public int CompareTo(ref FlatString16 other) => AsSpan(ref this).SequenceCompareTo(AsSpan(ref other));

    public int CompareTo(object? obj)
    {
        if (ReferenceEquals(null, obj))
        {
            return 1;
        }

        return obj is FlatString16 other
            ? CompareTo(ref other)
            : throw new ArgumentException($"Object must be of type {nameof(FlatString16)}");
    }

    public static bool operator <(FlatString16 left, FlatString16 right) => left.CompareTo(ref right) < 0;

    public static bool operator >(FlatString16 left, FlatString16 right) => left.CompareTo(ref right) > 0;

    public static bool operator <=(FlatString16 left, FlatString16 right) => left.CompareTo(ref right) <= 0;

    public static bool operator >=(FlatString16 left, FlatString16 right) => left.CompareTo(ref right) >= 0;

    #endregion

    #region Equals

    public bool Equals(FlatString16 other) => Equals(ref other);

    public bool Equals(ref FlatString16 other) => c0 == other.c0 && length == other.length &&
                                                  AsSpan(ref other).SequenceEqual(AsSpan(ref this));

    public override bool Equals(object? obj) => obj is FlatString16 other && Equals(ref other);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.AddBytes(AsSpan(ref this));
        return h.ToHashCode();
    }

    public static bool operator ==(FlatString16 left, FlatString16 right) => left.Equals(right);

    public static bool operator !=(FlatString16 left, FlatString16 right) => !left.Equals(right);

    #endregion

    public override string ToString() => Encoding.UTF8.GetString(AsSpan(ref this));
}