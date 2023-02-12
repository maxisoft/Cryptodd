using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cryptodd.IO;

namespace Cryptodd.OrderBooks;

public record struct DetailedOrderbookEntryFloatTuple(float Price, float Size, float RawSize, float MeanPrice,
    float ChangeCounter, float TotalChangeCounter, float SizeStd, float AggregateCount) : IComparable<DetailedOrderbookEntryFloatTuple>, IFloatSerializable
{
    public int CompareTo(DetailedOrderbookEntryFloatTuple other)
    {
        unsafe
        {
            var span = MemoryMarshal.CreateReadOnlySpan(ref this, 1);
            var otherSpan = MemoryMarshal.CreateReadOnlySpan(ref other, 1);

            return MemoryMarshal.Cast<DetailedOrderbookEntryFloatTuple, float>(span).SequenceCompareTo(
                MemoryMarshal.Cast<DetailedOrderbookEntryFloatTuple, float>(otherSpan));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public int WriteTo(Span<float> buffer)
    {
        var span = MemoryMarshal.CreateReadOnlySpan(ref this, 1);
        var floatSpan = MemoryMarshal.Cast<DetailedOrderbookEntryFloatTuple, float>(span);
        floatSpan.CopyTo(buffer);
        return floatSpan.Length;
    }

    public int ExpectedSize => 8;
}