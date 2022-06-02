using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cryptodd.Ftx.Models.Json;

public class PriceSizePairConverter : JsonConverter<PriceSizePair>
{
    public override PriceSizePair Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        double price, size;

        if (reader.TokenType == JsonTokenType.StartArray && reader.Read())
        {
            if (!reader.TryGetDouble(out price))
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
            if (!reader.TryGetDouble(out size))
            {
                throw new JsonException("unable to read size as double", null, null, reader.Position.GetInteger());
            }
        }
        else
        {
            throw new JsonException("unexpected end of stream", null, null, reader.Position.GetInteger());
        }

        if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
        {
            throw new JsonException("JsonArray expected to have only 2 elements", null, null,
                reader.Position.GetInteger());
        }

        return new PriceSizePair(price, size);
    }

    public override void Write(Utf8JsonWriter writer, PriceSizePair value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, new[] { value.Price, value.Size });
    }
}