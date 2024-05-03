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
    public override OkxHttpFundingRate Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        var stringConverter = (JsonConverter<PooledString>)options.GetConverter(typeof(PooledString));

        double fundingRate = default, nextFundingRate = default;
        long fundingTime = default, nextFundingTime = default;
        PooledString instId = default;
        PooledString instType = default;
        double maxFundingRate = default;
        PooledString method = default;
        double minFundingRate = default;
        double premium = default;
        double settFundingRate = default;
        PooledString settState = default;
        long ts = default;

        if (reader.TokenType != JsonTokenType.StartObject || !reader.Read())
        {
            throw new JsonException("Expecting a non-empty Object", null, null, reader.Position.GetInteger());
        }

        while (reader.TokenType != JsonTokenType.EndObject)
        {
            var propertyBytes = reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan;
            var propertyPosition = reader.Position.GetInteger();

            if (!reader.Read())
            {
                throw new JsonException("Unable to read property value", null, null, reader.Position.GetInteger());
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
                SafeJsonDoubleConverter<SafeJsonDoubleDefaultValue>.TryReadDouble(ref reader, out nextFundingRate);
            }
            else if (propertyBytes.SequenceEqual("nextFundingTime"u8))
            {
                Utf8JsonReaderUtils.TryGetInt64(ref reader, out nextFundingTime);
            }
            else if (propertyBytes.SequenceEqual("maxFundingRate"u8))
            {
                SafeJsonDoubleConverter<SafeJsonDoubleDefaultValue>.TryReadDouble(ref reader, out maxFundingRate);
            }
            else if (propertyBytes.SequenceEqual("method"u8))
            {
                method = stringConverter.Read(ref reader, typeof(PooledString), options);
            }
            else if (propertyBytes.SequenceEqual("minFundingRate"u8))
            {
                SafeJsonDoubleConverter<SafeJsonDoubleDefaultValue>.TryReadDouble(ref reader, out minFundingRate);
            }
            else if (propertyBytes.SequenceEqual("premium"u8))
            {
                SafeJsonDoubleConverter<SafeJsonDoubleDefaultValue>.TryReadDouble(ref reader, out premium);
            }
            else if (propertyBytes.SequenceEqual("settFundingRate"u8))
            {
                SafeJsonDoubleConverter<SafeJsonDoubleDefaultValue>.TryReadDouble(ref reader, out settFundingRate);
            }
            else if (propertyBytes.SequenceEqual("settState"u8))
            {
                settState = stringConverter.Read(ref reader, typeof(PooledString), options);
            }
            else if (propertyBytes.SequenceEqual("ts"u8))
            {
                Utf8JsonReaderUtils.TryGetInt64(ref reader, out ts);
            }
            else
            {
                throw new JsonException($"Unknown property \"{Encoding.UTF8.GetString(propertyBytes)}\"", null, null,
                    propertyPosition);
            }

            if (!reader.Read())
            {
                throw new JsonException("Unexpected end of stream", null, null, reader.Position.GetInteger());
            }
        }

        return new OkxHttpFundingRate(fundingRate, fundingTime, instId, instType, nextFundingRate, nextFundingTime,
            maxFundingRate, method, minFundingRate, premium, settFundingRate, settState, ts);
    }

    public override void Write(Utf8JsonWriter writer, OkxHttpFundingRate value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}