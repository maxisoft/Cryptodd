using System.Runtime.CompilerServices;

namespace Cryptodd.Json;

public readonly struct PooledString : IEquatable<PooledString>, IEquatable<string>, IComparable<PooledString>, IComparable
{
    private readonly string _string;

    public PooledString()
    {
        _string = "";
    }

    public PooledString(string s)
    {
        _string = s;
    }

    public int Length => _string.Length;
    public bool IsEmpty => string.IsNullOrEmpty(_string);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override string ToString() => _string;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ToString(IFormatProvider? provider) => _string.ToString(provider);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => _string.GetHashCode();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator string(PooledString pooledString) => pooledString._string;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator ReadOnlySpan<char>(PooledString pooledString) => pooledString._string;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator PooledString(string str) => new(str);
    
    #region CompareTo

    public int CompareTo(PooledString other) => string.Compare(_string, other._string, StringComparison.InvariantCulture);

    public int CompareTo(object? obj)
    {
        if (ReferenceEquals(null, obj))
        {
            return 1;
        }

        return obj is PooledString other ? CompareTo(other) : throw new ArgumentException($"Object must be of type {nameof(PooledString)}");
    }

    public static bool operator <(PooledString left, PooledString right) => left.CompareTo(right) < 0;

    public static bool operator >(PooledString left, PooledString right) => left.CompareTo(right) > 0;

    public static bool operator <=(PooledString left, PooledString right) => left.CompareTo(right) <= 0;

    public static bool operator >=(PooledString left, PooledString right) => left.CompareTo(right) >= 0;


    #endregion
    
    # region Equals

    public bool Equals(PooledString other) => _string == other._string;

    public bool Equals(string? other) => other is not null && Equals(new PooledString(other));

    public override bool Equals(object? obj) => (obj is PooledString other && Equals(other)) || (obj is string s && Equals(new PooledString(s)));

    public static bool operator ==(PooledString left, PooledString right) => left.Equals(right);

    public static bool operator !=(PooledString left, PooledString right) => !left.Equals(right);

    #endregion
}