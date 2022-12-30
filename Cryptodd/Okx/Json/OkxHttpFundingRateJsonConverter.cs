using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cryptodd.Json;
using Cryptodd.Json.Converters;
using Cryptodd.Okx.Http;
using Cryptodd.Okx.Models;
using Cryptodd.Utils;

namespace Cryptodd.Okx.Json;

public class OkxHttpFundingRateJsonConverter : JsonConverter<OkxHttpFundingRate>
{
    internal static StringPool StringPool => OkxPublicHttpApi.StringPool;

    public override OkxHttpFundingRate Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        var stringConverter = (JsonConverter<PooledString>)options.GetConverter(typeof(PooledString));

        double fundingRate = default, nextFundingRate = default;
        long fundingTime = default, nextFundingTime = default;
        PooledString instId = default;
        PooledString instType = default;

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
                    var propertyBytes = reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan;
                    var propertyPosition = reader.Position.GetInteger();

                    if (!reader.Read())
                    {
                        throw new JsonException("unable to read property value", null, null,
                            reader.Position.GetInteger());
                    }

                    if (propertyBytes.SequenceEqual("fundingRate"u8))
                    {
                        SafeJsonDoubleConverter<SafeJsonDoubleDefaultValue>.TryReadDouble(ref reader, out fundingRate);
                    }
                    else if (propertyBytes.SequenceEqual("fundingTime"u8))
                    {
                        Utf8JsonReaderUtils.TryGetInt64(ref reader, out fundingTime);
                    }
                    else if (propertyBytes.SequenceEqual("instId"u8))
                    {
                        instId = stringConverter.Read(ref reader, typeof(PooledString), options);
                    }
                    else if (propertyBytes.SequenceEqual("instType"u8))
                    {
                        instType = stringConverter.Read(ref reader, typeof(PooledString), options);
                    }
                    else if (propertyBytes.SequenceEqual("nextFundingRate"u8))
                    {
                        SafeJsonDoubleConverter<SafeJsonDoubleDefaultValue>.TryReadDouble(ref reader,
                            out nextFundingRate);
                    }
                    else if (propertyBytes.SequenceEqual("nextFundingTime"u8))
                    {
                        Utf8JsonReaderUtils.TryGetInt64(ref reader, out nextFundingTime);
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

        return new OkxHttpFundingRate(fundingRate, fundingTime, instId,
            instType, nextFundingRate, nextFundingTime);
    }

    public override void Write(Utf8JsonWriter writer, OkxHttpFundingRate value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}