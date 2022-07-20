using System.Text.Json;
using System.Text.Json.Serialization;
using Maxisoft.Utils.Collections.Lists.Specialized;

namespace Cryptodd.Ftx.Models.Json;

public class PooledListConverter<T> : JsonConverter<PooledList<T>>
{
    public int DefaultCapacity { get; set; }

    public override PooledList<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var converter = (JsonConverter<T>)options.GetConverter(typeof(T));

        var res = new PooledList<T>(DefaultCapacity);

        if (reader.TokenType != JsonTokenType.StartArray || !reader.Read())
        {
            throw new JsonException("expecting a non empty StartArray", null, null, reader.Position.GetInteger());
        }

        while (reader.TokenType != JsonTokenType.EndArray)
        {
            res.Add(converter.Read(ref reader, typeof(T), options)!);
            if (!reader.Read())
            {
                throw new JsonException("unexpected end of stream", null, null, reader.Position.GetInteger());
            }
        }

        if (reader.TokenType != JsonTokenType.EndArray)
        {
            throw new JsonException("expecting EndArray", null, null, reader.Position.GetInteger());
        }
        return res;
    }

    public override void Write(Utf8JsonWriter writer, PooledList<T> value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}