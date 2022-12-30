using System.Text.Json;
using System.Text.Json.Serialization;
using Cryptodd.Utils;

namespace Cryptodd.Json.Converters;

public class SafeJsonDoubleConverter<T> : JsonConverter<SafeJsonDouble<T>> where T : struct, ISafeJsonDoubleDefaultValue
{

    public override SafeJsonDouble<T> Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options) =>
        !Utf8JsonReaderUtils.TryGetDouble(ref reader, out var res) ? new SafeJsonDouble<T>() : res;

    public override void Write(Utf8JsonWriter writer, SafeJsonDouble<T> value,
        JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value.Value);
    }
}