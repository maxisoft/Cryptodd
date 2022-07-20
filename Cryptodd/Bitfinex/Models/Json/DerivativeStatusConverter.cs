using System.Text.Json;
using System.Text.Json.Serialization;
using Maxisoft.Utils.Collections.Lists.Specialized;

namespace Cryptodd.Bitfinex.Models.Json;

public class DerivativeStatusConverter : JsonConverter<DerivativeStatus>
{
    public override DerivativeStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string key;

        if (reader.TokenType == JsonTokenType.StartArray && reader.Read())
        {
            if (reader.TokenType != JsonTokenType.String || string.IsNullOrEmpty(reader.GetString()))
            {
                throw new JsonException("unable to read channel as int64", null, null, reader.Position.GetInteger());
            }

            key = reader.GetString()!;
        }
        else
        {
            throw new JsonException("expecting a non empty JsonArray", null, null, reader.Position.GetInteger());
        }

        if (!reader.Read() || !reader.TryGetInt64(out var time))
        {
            throw new JsonException("unexpected end of stream", null, null, reader.Position.GetInteger());
        }

        int SkipNulls(ref Utf8JsonReader reader, int maxCount = int.MaxValue)
        {
            int c = 0;
            if (c > maxCount)
            {
                return 0;
            }

            do
            {
                if (!reader.Read())
                {
                    throw new JsonException("unexpected end of stream", null, null, reader.Position.GetInteger());
                }
            } while (reader.TokenType == JsonTokenType.Null && c++ < maxCount);

            return c;
        }

        SkipNulls(ref reader);
        if (!reader.TryGetDouble(out var derivativeMidPrice))
        {
            throw new JsonException($"unable to read {nameof(derivativeMidPrice)}", null, null,
                reader.Position.GetInteger());
        }

        if (!reader.Read())
        {
            throw new JsonException("unexpected end of stream", null, null, reader.Position.GetInteger());
        }

        if (!reader.TryGetDouble(out var spotPrice))
        {
            throw new JsonException($"unable to read {nameof(spotPrice)}", null, null, reader.Position.GetInteger());
        }

        SkipNulls(ref reader, 1);
        if (!reader.TryGetDouble(out var insuranceFundBalance))
        {
            throw new JsonException($"unable to read {nameof(insuranceFundBalance)}", null, null,
                reader.Position.GetInteger());
        }

        SkipNulls(ref reader, 1);
        if (!reader.TryGetInt64(out var nextFundingEvtTimestampMs))
        {
            throw new JsonException($"unable to read {nameof(nextFundingEvtTimestampMs)}", null, null,
                reader.Position.GetInteger());
        }

        if (!reader.Read())
        {
            throw new JsonException("unexpected end of stream", null, null, reader.Position.GetInteger());
        }

        if (!reader.TryGetDouble(out var nextFundingAccrued))
        {
            throw new JsonException($"unable to read {nameof(nextFundingAccrued)}", null, null,
                reader.Position.GetInteger());
        }

        if (!reader.Read())
        {
            throw new JsonException("unexpected end of stream", null, null, reader.Position.GetInteger());
        }

        if (!reader.TryGetInt32(out var nextFundingStep))
        {
            throw new JsonException($"unable to read {nameof(nextFundingStep)}", null, null,
                reader.Position.GetInteger());
        }

        SkipNulls(ref reader, 1);
        if (!reader.TryGetDouble(out var currentFunding))
        {
            throw new JsonException($"unable to read {nameof(currentFunding)}", null, null,
                reader.Position.GetInteger());
        }

        SkipNulls(ref reader, 2);
        if (!reader.TryGetDouble(out var markPrice))
        {
            throw new JsonException($"unable to read {nameof(markPrice)}", null, null, reader.Position.GetInteger());
        }

        SkipNulls(ref reader, 2);
        double openInterest = 0;
        if (reader.TokenType != JsonTokenType.Null && !reader.TryGetDouble(out openInterest))
        {
            throw new JsonException($"unable to read {nameof(openInterest)}", null, null, reader.Position.GetInteger());
        }

        SkipNulls(ref reader, 3);
        double clampMin = 0;
        if (reader.TokenType != JsonTokenType.Null && !reader.TryGetDouble(out clampMin))
        {
            throw new JsonException($"unable to read {nameof(clampMin)}", null, null, reader.Position.GetInteger());
        }

        if (!reader.Read())
        {
            throw new JsonException("unexpected end of stream", null, null, reader.Position.GetInteger());
        }

        double clampMax = 0;
        if (reader.TokenType != JsonTokenType.Null && !reader.TryGetDouble(out clampMax))
        {
            throw new JsonException($"unable to read {nameof(clampMax)}", null, null, reader.Position.GetInteger());
        }

        if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
        {
            throw new JsonException("EndArray expected", null, null,
                reader.Position.GetInteger());
        }

        return new DerivativeStatus(
            Key: key,
            Time: time,
            DerivativeMidPrice: derivativeMidPrice,
            SpotPrice: spotPrice,
            InsuranceFundBalance: insuranceFundBalance,
            NextFundingEvtTimestampMs: nextFundingEvtTimestampMs,
            NextFundingAccrued: nextFundingAccrued,
            NextFundingStep: nextFundingStep,
            CurrentFunding: currentFunding,
            MarkPrice: markPrice,
            OpenInterest: openInterest,
            ClampMin: clampMin,
            ClampMax: clampMax
        );
    }

    public override void Write(Utf8JsonWriter writer, DerivativeStatus value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}