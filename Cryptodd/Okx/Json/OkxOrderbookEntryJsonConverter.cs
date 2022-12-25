using System.Buffers;
using System.Buffers.Text;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cryptodd.Binance.Models;
using Cryptodd.Okx.Models;
using Cryptodd.Utils;

namespace Cryptodd.Binance.Json;

public class OkxOrderbookEntryJsonConverter : JsonConverter<OkxOrderbookEntry>
{
    private static bool TryGetDouble(ref Utf8JsonReader reader, out double res) =>
        Utf8JsonReaderUtils.TryGetDouble(ref reader, out res);

    private static bool TryGetInt64(ref Utf8JsonReader reader, out long res) =>
        Utf8JsonReaderUtils.TryGetInt64(ref reader, out res);

    public override OkxOrderbookEntry Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        double price, quantity;
        long count, tmp;
        var done = false;

        if (reader.TokenType == JsonTokenType.StartArray && reader.Read())
        {
            if (!TryGetDouble(ref reader, out price))
            {
                throw new JsonException("unable to read price as double", null, null, reader.Position.GetInteger());
            }
        }
        else
        {
            throw new JsonException("expecting a non empty JsonArray", null, null, reader.Position.GetInteger());
        }

        if (reader.Read())
        {
            if (!TryGetDouble(ref reader, out quantity))
            {
                throw new JsonException("unable to read quantity as double", null, null, reader.Position.GetInteger());
            }
        }
        else
        {
            throw new JsonException("unexpected end of stream", null, null, reader.Position.GetInteger());
        }

        if (reader.Read())
        {
            if (!TryGetDouble(ref reader, out _))
            {
                throw new JsonException("unable to read deprecated feature as double", null, null,
                    reader.Position.GetInteger());
            }
        }
        else
        {
            throw new JsonException("unexpected end of stream", null, null, reader.Position.GetInteger());
        }

        if (reader.Read())
        {
            if (!TryGetInt64(ref reader, out tmp))
            {
                throw new JsonException("unable to read count or deprecated feature as long", null, null,
                    reader.Position.GetInteger());
            }
        }
        else
        {
            throw new JsonException("unexpected end of stream", null, null, reader.Position.GetInteger());
        }

        if (reader.Read())
        {
            if (reader.TokenType is JsonTokenType.EndArray)
            {
                count = tmp;
                done = true;
            }
            else
            {
                if (!TryGetInt64(ref reader, out count))
                {
                    throw new JsonException("unable to read count as long", null, null,
                        reader.Position.GetInteger());
                }
            }
        }
        else
        {
            throw new JsonException("unexpected end of stream", null, null, reader.Position.GetInteger());
        }

        if (!done)
        {
            if (!reader.Read() || reader.TokenType is not JsonTokenType.EndArray)
            {
                throw new JsonException("JsonArray expected to only have 3 or 4 elements", null, null,
                    reader.Position.GetInteger());
            }

            done = true;
        }


        if (count > int.MaxValue)
        {
            count = int.MaxValue;
        }

        return new OkxOrderbookEntry(price, quantity, (int)count);
    }

    public override void Write(Utf8JsonWriter writer, OkxOrderbookEntry value,
        JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, new[] { value.Price, value.Quantity, value.Count });
    }
}