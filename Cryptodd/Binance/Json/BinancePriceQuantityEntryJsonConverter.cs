using System.Buffers;
using System.Buffers.Text;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cryptodd.Binance.Models;
using Cryptodd.Utils;

namespace Cryptodd.Binance.Json;

public class BinancePriceQuantityEntryJsonConverter : JsonConverter<BinancePriceQuantityEntry<double>>
{
    private static bool TryGetDouble(ref Utf8JsonReader reader, out double res) =>
        Utf8JsonReaderUtils.TryGetDouble(ref reader, out res);

    public override BinancePriceQuantityEntry<double> Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        double price, quantity;

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

        if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
        {
            throw new JsonException("JsonArray expected to only have 2 elements", null, null,
                reader.Position.GetInteger());
        }

        return new BinancePriceQuantityEntry<double>(price, quantity);
    }

    public override void Write(Utf8JsonWriter writer, BinancePriceQuantityEntry<double> value,
        JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, new[] { value.Price, value.Quantity });
    }
}