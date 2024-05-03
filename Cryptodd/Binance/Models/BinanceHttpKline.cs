using System.Diagnostics;
using Cryptodd.IO;

namespace Cryptodd.Binance.Models;

/***

BinanceHttpKline is a readonly record that represents the aggregated trades data from the Binance exchange.

It has the following fields:

- OpenTime: the time the order was placed

- Open: the price the order was placed at

- High: the highest price the order was placed at

- Low: the lowest price the order was placed at

- Close: the price the order was filled at

- CloseTime: the time the order was filled

- QuoteAssetVolume: the total volume of the base asset being traded

- NumberOfTrades: the number of trades that were executed as part of this order

- TakerBuyBaseAssetVolume: the total volume of the base asset bought by the taker

- TakerBuyQuoteAssetVolume: the total volume of the quote asset bought by the taker

*/
public readonly record struct BinanceHttpKline(
    long OpenTime,
    double Open,
    double High,
    double Low,
    double Close,
    double Volume,
    long CloseTime,
    double QuoteAssetVolume,
    int NumberOfTrades,
    double TakerBuyBaseAssetVolume,
    double TakerBuyQuoteAssetVolume
): IDoubleSerializable
{
    public int WriteTo(Span<double> buffer)
    {
        var size = ExpectedSize;
        if (Const.IsDebug)
        {
            buffer[0] = OpenTime;
            buffer[1] = Open;
            buffer[2] = High;
            buffer[3] = Low;
            buffer[4] = Close;
            buffer[5] = Volume;
            buffer[6] = CloseTime;
            buffer[7] = QuoteAssetVolume;
            buffer[8] = NumberOfTrades;
            buffer[9] = TakerBuyBaseAssetVolume;
            buffer[10] = TakerBuyQuoteAssetVolume;
            return size;
        }
        else if (buffer.Length >= size)
        {
            unsafe
            {
                fixed (double* b = buffer)
                {
                    b[0] = OpenTime;
                    b[1] = Open;
                    b[2] = High;
                    b[3] = Low;
                    b[4] = Close;
                    b[5] = Volume;
                    b[6] = CloseTime;
                    b[7] = QuoteAssetVolume;
                    b[8] = NumberOfTrades;
                    b[9] = TakerBuyBaseAssetVolume;
                    b[10] = TakerBuyQuoteAssetVolume;
                }
            }
            return size;
        }

        return 0;
    }

    public int ExpectedSize => 11;
}