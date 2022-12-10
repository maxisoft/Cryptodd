using System.Runtime.CompilerServices;

namespace Cryptodd.OrderBooks;

public readonly record struct PriceSizeCountTriplet(float Price, float Size, int Count) : IComparable<PriceSizeCountTriplet>, IFloatSerializable
{
    public int CompareTo(PriceSizeCountTriplet other)
    {
        var priceComparison = Price.CompareTo(other.Price);
        if (priceComparison != 0)
        {
            return priceComparison;
        }

        var sizeComparison = Size.CompareTo(other.Size);
        // ReSharper disable once ConvertIfStatementToReturnStatement
        if (sizeComparison != 0)
        {
            return sizeComparison;
        }

        return Count.CompareTo(other.Count);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public int WriteTo(Span<float> buffer)
    {
#if (DEBUG || SAFE_WRITE_TO)
        buffer[0] = Price;
        buffer[1] = Size;
        buffer[2] = Count;
#else
        unsafe
        {
            fixed (float* bp = buffer)
            {
                bp[0] = Price;
                bp[1] = Size;
                bp[2] = Count;
            }
        }
#endif
        return 3;
    }

    public int ExpectedSize => 3;
}