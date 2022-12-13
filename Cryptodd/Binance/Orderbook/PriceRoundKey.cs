using System.Runtime.CompilerServices;

namespace Cryptodd.Binance.Orderbook;

public readonly record struct PriceRoundKey(double Value) : IComparable<PriceRoundKey>, IComparable
{
    public const int RoundedDigit = 8;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => Value.GetHashCode();

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static PriceRoundKey CreateFromPrice(double price)
    {
        var log = Math.Log2(price); // Use log2 as it should be faster than log10
        var value = Math.Round(log, RoundedDigit, MidpointRounding.ToZero);
        return new PriceRoundKey(value);
    }

    public double RoundedPrice => Math.Pow(2, Value);

    public int CompareTo(PriceRoundKey other) => Value.CompareTo(other.Value);

    public int CompareTo(object? obj)
    {
        if (ReferenceEquals(null, obj))
        {
            return 1;
        }

        return obj is PriceRoundKey other
            ? CompareTo(other)
            : throw new ArgumentException($"Object must be of type {nameof(PriceRoundKey)}");
    }

    public static bool operator <(PriceRoundKey left, PriceRoundKey right) => left.CompareTo(right) < 0;

    public static bool operator >(PriceRoundKey left, PriceRoundKey right) => left.CompareTo(right) > 0;

    public static bool operator <=(PriceRoundKey left, PriceRoundKey right) => left.CompareTo(right) <= 0;

    public static bool operator >=(PriceRoundKey left, PriceRoundKey right) => left.CompareTo(right) >= 0;
}