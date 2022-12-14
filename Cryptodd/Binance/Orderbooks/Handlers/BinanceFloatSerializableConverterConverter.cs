using System.Runtime.CompilerServices;
using Cryptodd.OrderBooks;

namespace Cryptodd.Binance.Orderbooks.Handlers;

public struct
    BinanceFloatSerializableConverterConverter : IFloatSerializableConverter<DetailedOrderbookEntryFloatTuple,
        DetailedOrderbookEntryFloatTuple>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public DetailedOrderbookEntryFloatTuple Convert(in DetailedOrderbookEntryFloatTuple priceSizePair) => priceSizePair;
}