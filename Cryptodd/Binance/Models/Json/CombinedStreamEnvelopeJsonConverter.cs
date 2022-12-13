using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Cryptodd.Ftx.Models.Json;
using Cryptodd.Json;
using Maxisoft.Utils.Collections.Lists.Specialized;

namespace Cryptodd.Binance.Models.Json;

public class CombinedStreamEnvelopeJsonConverter<T> : JsonConverter<CombinedStreamEnvelope<T>>
{
    internal static StringPool StringPool => PreParsedCombinedStreamEventParser.StringPool;

    public override CombinedStreamEnvelope<T> Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        var tConverter = (JsonConverter<T>)options.GetConverter(typeof(T));
        string? stream = null;
        T? data = default;

        if (reader.TokenType != JsonTokenType.StartObject || !reader.Read())
        {
            throw new JsonException("expecting a non empty StartArray", null, null, reader.Position.GetInteger());
        }

        while (reader.TokenType != JsonTokenType.EndObject)
        {
            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    var propertyBytes = reader.ValueSpan;
                    var propertyPosition = reader.Position.GetInteger();

                    if (!reader.Read())
                    {
                        throw new JsonException("unable to read property value", null, null,
                            reader.Position.GetInteger());
                    }

                    if (propertyBytes.SequenceEqual("stream"u8))
                    {
                        if (!StringPool.TryGetString(reader.ValueSpan, out stream))
                        {
                            stream = reader.GetString() ?? throw new JsonException(
                                "unable to read property value stream", null, null,
                                reader.Position.GetInteger());
                        }
                    }
                    else if (propertyBytes.SequenceEqual("data"u8))
                    {
                        data = tConverter.Read(ref reader, typeof(T), options) ?? throw new JsonException(
                            "unable to read/convert property value data", null, null,
                            reader.Position.GetInteger());
                    }
                    else
                    {
                        throw new JsonException($"unknown property \"{Encoding.UTF8.GetString(propertyBytes)}\"", null,
                            null,
                            propertyPosition);
                    }

                    break;
                default:
                    throw new JsonException(
                        $"unexpected token {reader.TokenType} expected {JsonTokenType.PropertyName}", null, null,
                        reader.Position.GetInteger());
            }

            if (!reader.Read())
            {
                throw new JsonException("unexpected end of stream", null, null, reader.Position.GetInteger());
            }
        }

        return new CombinedStreamEnvelope<T>(
            stream ?? throw new JsonException("unable to get stream", null, null, reader.Position.GetInteger()),
            data ?? throw new JsonException("unable to get data", null, null, reader.Position.GetInteger()));
    }

    public override void Write(Utf8JsonWriter writer, CombinedStreamEnvelope<T> value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}