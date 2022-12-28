using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cryptodd.Utils;

namespace Cryptodd.Json.Converters;

public class JsonDoubleConverter : JsonConverter<JsonDouble>
{
    public override JsonDouble Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (!Utf8JsonReaderUtils.TryGetDouble(ref reader, out var res))
        {
            throw new JsonException("unable to handle double", null, null, reader.Position.GetInteger());
        }

        return res;
    }

    public override void Write(Utf8JsonWriter writer, JsonDouble value,
        JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value.Value);
    }
}