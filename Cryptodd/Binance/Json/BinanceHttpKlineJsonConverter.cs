using System.Buffers;
using System.Buffers.Text;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cryptodd.Binance.Models;
using Cryptodd.Utils;

namespace Cryptodd.Binance.Json;

public class BinanceHttpKlineJsonConverter : JsonConverter<BinanceHttpKline>
{
    private static bool TryGetDouble(ref Utf8JsonReader reader, out double res) =>
        Utf8JsonReaderUtils.TryGetDouble(ref reader, out res);

    private static bool TryGetInt64(ref Utf8JsonReader reader, out long res) =>
        Utf8JsonReaderUtils.TryGetInt64(ref reader, out res);

    public override BinanceHttpKline Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        long openTime;
        double open;
        double high;
        double low;
        double close;
        double volume;
        long closeTime;
        double quoteAssetVolume;
        long numberOfTrades;
        double takerBuyBaseAssetVolume;
        double takerBuyQuoteAssetVolume;

        if (reader.TokenType == JsonTokenType.StartArray && reader.Read())
        {
            if (!TryGetInt64(ref reader, out openTime))
            {
                throw new JsonException("unable to read openTime", null, null, reader.Position.GetInteger());
            }

            if (!reader.Read() || !TryGetDouble(ref reader, out open))
            {
                throw new JsonException("unable to read open", null, null, reader.Position.GetInteger());
            }

            if (!reader.Read() || !TryGetDouble(ref reader, out high))
            {
                throw new JsonException("unable to read high", null, null, reader.Position.GetInteger());
            }

            if (!reader.Read() || !TryGetDouble(ref reader, out low))
            {
                throw new JsonException("unable to read low", null, null, reader.Position.GetInteger());
            }

            if (!reader.Read() || !TryGetDouble(ref reader, out close))
            {
                throw new JsonException("unable to read close", null, null, reader.Position.GetInteger());
            }

            if (!reader.Read() || !TryGetDouble(ref reader, out volume))
            {
                throw new JsonException("unable to read volume", null, null, reader.Position.GetInteger());
            }

            if (!reader.Read() || !TryGetInt64(ref reader, out closeTime))
            {
                throw new JsonException("unable to read closeTime", null, null, reader.Position.GetInteger());
            }

            if (!reader.Read() || !TryGetDouble(ref reader, out quoteAssetVolume))
            {
                throw new JsonException("unable to read quoteAssetVolume", null, null, reader.Position.GetInteger());
            }

            if (!reader.Read() || !TryGetInt64(ref reader, out numberOfTrades))
            {
                throw new JsonException("unable to read numberOfTrades", null, null, reader.Position.GetInteger());
            }

            if (!reader.Read() || !TryGetDouble(ref reader, out takerBuyBaseAssetVolume))
            {
                throw new JsonException("unable to read takerBuyBaseAssetVolume", null, null,
                    reader.Position.GetInteger());
            }

            if (!reader.Read() || !TryGetDouble(ref reader, out takerBuyQuoteAssetVolume))
            {
                throw new JsonException("unable to read takerBuyQuoteAssetVolume", null, null,
                    reader.Position.GetInteger());
            }

            do
            {
                if (!reader.Read())
                {
                    throw new JsonException("unexpected end of stream", null, null, reader.Position.GetInteger());
                }

                if (reader.TokenType is JsonTokenType.String or JsonTokenType.Number)
                {
                    reader.TrySkip();
                }
                else if (reader.TokenType is JsonTokenType.EndArray)

                {
                    break;
                }
                else
                {
                    throw new JsonException("", null, null,
                        reader.Position.GetInteger());
                }
            } while (reader.TokenType is not JsonTokenType.EndArray);
        }
        else
        {
            throw new JsonException("expecting a non empty JsonArray", null, null, reader.Position.GetInteger());
        }

        if (reader.TokenType is not JsonTokenType.EndArray)
        {
            throw new JsonException("JsonArray expected to ends", null, null,
                reader.Position.GetInteger());
        }

        return new BinanceHttpKline(
            openTime,
            open,
            high,
            low,
            close,
            volume,
            closeTime,
            quoteAssetVolume,
            checked((int)numberOfTrades),
            takerBuyBaseAssetVolume,
            takerBuyQuoteAssetVolume
        );
    }

    public override void Write(Utf8JsonWriter writer, BinanceHttpKline value,
        JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}