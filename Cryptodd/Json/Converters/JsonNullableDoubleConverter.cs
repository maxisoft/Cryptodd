using System.Text.Json;
using System.Text.Json.Serialization;
using Cryptodd.Utils;

namespace Cryptodd.Json.Converters;

public class JsonNullableDoubleConverter : JsonConverter<JsonNullableDouble>
{
    public override JsonNullableDouble Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.Null)
        {
            return new JsonNullableDouble(null);
        }
        if (!Utf8JsonReaderUtils.TryGetDouble(ref reader, out var res))
        {
            if (reader is { TokenType: JsonTokenType.String, HasValueSequence: false, ValueSpan.IsEmpty: true })
            {
                return new JsonNullableDouble(null);
            }
            throw new JsonException("unable to handle double", null, null, reader.Position.GetInteger());
        }

        return res;
    }

    public override void Write(Utf8JsonWriter writer, JsonNullableDouble value,
        JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value.Value);
    }
}