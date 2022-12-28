using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cryptodd.Json.Converters;

public class PooledStringJsonConverter : JsonConverter<PooledString>
{
    private StringPool _stringPool;

    public PooledStringJsonConverter(int stringPoolSize) : this(new StringPool(stringPoolSize)) { }

    public PooledStringJsonConverter(StringPool stringPool)
    {
        _stringPool = stringPool;
    }

    public bool Strict { get; set; }

    public NumberFormatInfo NumberFormatInfo { get; set; } = NumberFormatInfo.InvariantInfo;

    public StringPool StringPool
    {
        get => _stringPool;
        set => _stringPool = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string ReadString(ref Utf8JsonReader reader, in StringPool stringPool)
    {
        if (!reader.HasValueSequence && stringPool.TryGetString(reader.ValueSpan, out var s))
        {
            return s;
        }

        return reader.GetString() ??
               throw new JsonException("unable to handle string", null, null, reader.Position.GetInteger());
    }

    public override PooledString Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (Strict)
        {
            if (reader.TokenType is not JsonTokenType.String)
            {
                throw new JsonException($"unable to read string from {reader.TokenType}", null, null, reader.Position.GetInteger());
            }

            return ReadString(ref reader, in _stringPool);
        }

        // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return ReadString(ref reader, in _stringPool);
            case JsonTokenType.Number:
                if (reader.TryGetInt64(out var value))
                {
                    return new PooledString(value.ToString(NumberFormatInfo));
                }

                if (reader.TryGetDouble(out var doubleValue))
                {
                    return new PooledString(doubleValue.ToString(NumberFormatInfo));
                }

                throw new JsonException("unable to parse number", null, null, reader.Position.GetInteger());
            case JsonTokenType.True:
                return new PooledString("true");
            case JsonTokenType.False:
                return new PooledString("false");
            case JsonTokenType.Null:
                return new PooledString("");
            default:
                throw new JsonException($"unable to interpret {reader.TokenType} as string", null, null,
                    reader.Position.GetInteger());
        }
    }

    public override void Write(Utf8JsonWriter writer, PooledString value,
        JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value.ToString());
    }
}