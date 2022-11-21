using System.Runtime.CompilerServices;

namespace Cryptodd.OrderBooks;

public readonly record struct PriceSizePair(float Price, float Size) : IComparable<PriceSizePair>, IFloatSerializable
{
    public int CompareTo(PriceSizePair other)
    {
        var priceComparison = Price.CompareTo(other.Price);
        return priceComparison != 0 ? priceComparison : Size.CompareTo(other.Size);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public int WriteTo(Span<float> buffer)
    {
#if (DEBUG || SAFE_WRITE_TO)
        buffer[0] = Price;
        buffer[1] = Size;
#else
        unsafe
        {
            fixed (float* bp = buffer)
            {
                bp[0] = Price;
                bp[1] = Size;
            }
        }
#endif
        return 2;
    }
    
    public int ExpectedSize => 2;
}