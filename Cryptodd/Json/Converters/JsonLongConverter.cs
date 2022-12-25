using System.Text.Json;
using System.Text.Json.Serialization;
using Cryptodd.Utils;

namespace Cryptodd.Json.Converters;

public class JsonLongConverter : JsonConverter<JsonLong>
{

    public override JsonLong Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (!Utf8JsonReaderUtils.TryGetInt64(ref reader, out var res))
        {
            throw new JsonException("unable to handle long", null, null, reader.Position.GetInteger());
        }

        return res;
    }

    public override void Write(Utf8JsonWriter writer, JsonLong value,
        JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value.Value);
    }
}