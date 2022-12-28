using System.Runtime.CompilerServices;
using Cryptodd.Binance.Orderbooks.Handlers;
using Cryptodd.IoC;
using Cryptodd.Okx.Models;
using Cryptodd.OrderBooks;

namespace Cryptodd.Okx.Orderbooks.Handlers;

public struct
    OkxFloatSerializableConverterConverter : IFloatSerializableConverter<OkxOrderbookEntry,
        PriceSizeCountTriplet>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public PriceSizeCountTriplet Convert(in OkxOrderbookEntry priceSizePair) =>
        new PriceSizeCountTriplet((float)priceSizePair.Price, (float)priceSizePair.Quantity, priceSizePair.Count);
}