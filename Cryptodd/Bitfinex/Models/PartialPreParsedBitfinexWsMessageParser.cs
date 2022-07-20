using System.Globalization;
using System.Text;
using System.Text.Json;
using Cryptodd.Ftx.Models;

namespace Cryptodd.Bitfinex.Models;

public class PartialPreParsedBitfinexWsMessageParser
{
    private static readonly JsonReaderOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false
    };

    public static bool TryParse(ReadOnlySpan<byte> bytes, out PreParsedBitfinexWsMessage result)
    {
        var @event = "";
        var channel = "";
        var symbol = "";
        var code = "";
        var msg = "";
        long chanId = 0;
        var subId = string.Empty;
        var isArray = false;
        var isHearthBeat = false;

        var done = false;

        #region ConsumeJson

        // ReSharper disable once VariableHidesOuterVariable
        void ConsumeJson(ReadOnlySpan<byte> bytes)
        {
            var reader = new Utf8JsonReader(bytes, Options);

            var startObjectCounter = 0;

            while (reader.Read() && !done)
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        startObjectCounter++;
                        if (startObjectCounter > 1)
                        {
                            reader.Skip();
                        }

                        break;
                    case JsonTokenType.EndObject:
                        startObjectCounter--;
                        break;
                    case JsonTokenType.StartArray:
                        if (startObjectCounter <= 0)
                        {
                            isArray = true;
                            done = true;

                            if (!reader.Read())
                            {
                                return;
                            }

                            chanId = reader.GetInt64();
                            if (!reader.Read())
                            {
                                return;
                            }

                            if (reader.TokenType == JsonTokenType.String &&
                                reader.ValueSpan.SequenceEqual(Encoding.UTF8.GetBytes("hb")))
                            {
                                isHearthBeat = true;
                            }

                            done = true;
                            return;
                        }


                        reader.Skip();
                        break;
                    case JsonTokenType.PropertyName:
                        if (startObjectCounter <= 0)
                        {
                            throw new JsonException($"Unexpected {reader.TokenType}", string.Empty, 0,
                                reader.BytesConsumed);
                        }

                        if (startObjectCounter == 1)
                        {
                            var property = reader.GetString();
                            if (string.IsNullOrWhiteSpace(property))
                            {
                                return;
                            }

                            if (!reader.Read())
                            {
                                return;
                            }

                            switch (property)
                            {
                                case "event":
                                    if (reader.TokenType != JsonTokenType.String)
                                    {
                                        return;
                                    }

                                    @event = reader.GetString();
                                    break;
                                case "chanId":
                                    if (reader.TokenType != JsonTokenType.Number)
                                    {
                                        return;
                                    }

                                    chanId = reader.GetInt64();
                                    break;
                                case "subId":
                                    if (reader.TokenType is not (JsonTokenType.Number or JsonTokenType.String))
                                    {
                                        return;
                                    }

                                    subId = reader.GetString() ?? string.Empty;
                                    break;
                                case "channel":
                                    if (reader.TokenType != JsonTokenType.String)
                                    {
                                        return;
                                    }

                                    channel = reader.GetString();
                                    break;
                                case "code":
                                    if (reader.TokenType is JsonTokenType.String)
                                    {
                                        code = reader.GetString();
                                        break;
                                    }

                                    if (reader.TokenType is JsonTokenType.Number)
                                    {
                                        code = reader.GetInt64().ToString(CultureInfo.InvariantCulture);
                                        break;
                                    }

                                    return;
                                case "msg":
                                case "message":
                                    if (reader.TokenType != JsonTokenType.String)
                                    {
                                        return;
                                    }

                                    msg = reader.GetString();
                                    break;
                                case "symbol":
                                    if (reader.TokenType != JsonTokenType.String)
                                    {
                                        return;
                                    }

                                    symbol = reader.GetString();
                                    break;
                                case "data":
                                    done = !string.IsNullOrEmpty(@event);
                                    return;
                                default:
                                    reader.Skip();
                                    break;
                            }
                        }

                        break;
                }
            }

            done |= reader.BytesConsumed == bytes.Length && (isArray || !string.IsNullOrEmpty(@event));
        }

        #endregion

        try
        {
            ConsumeJson(bytes);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException)
        {
            done = false;
        }


        result = new PreParsedBitfinexWsMessage(
            Event: @event,
            Channel: channel,
            ChanId: chanId,
            SubId: subId,
            Symbol: symbol,
            Code: code,
            Msg: msg,
            IsArray: isArray,
            IsHearthBeat: isHearthBeat);
        return done && (isArray || !string.IsNullOrWhiteSpace(@event));
    }
}