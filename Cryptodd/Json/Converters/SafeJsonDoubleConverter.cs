using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cryptodd.Utils;

namespace Cryptodd.Json.Converters;

public class SafeJsonDoubleConverter<T> : JsonConverter<SafeJsonDouble<T>> where T : struct, ISafeJsonDoubleDefaultValue
{

    public override SafeJsonDouble<T> Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        TryReadDouble(ref reader, out var result);
        return result;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryReadDouble(ref Utf8JsonReader reader, out double result)
    {
        if (Utf8JsonReaderUtils.TryGetDouble(ref reader, out result))
        {
            return true;
        }
        else
        {
            result = new T().GetDefault();
            return false;
        }
        
    }

    public override void Write(Utf8JsonWriter writer, SafeJsonDouble<T> value,
        JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value.Value);
    }
}