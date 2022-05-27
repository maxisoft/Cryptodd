using System.Globalization;
using System.Text.Json;

namespace CryptoDumper.Ftx.Models.Json;

public struct PartialPreParsedFtxWsMessageParser
{
    private static readonly JsonReaderOptions Options = new JsonReaderOptions
    {
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false
    };
    
    public static bool TryParse(ReadOnlySpan<byte> bytes, out PreParsedFtxWsMessage result)
    {
        var type = "";
        var channel = "";
        var market = "";
        var code = "";
        var msg = "";
        long? checksum = null;
        double? grouping = null;
        DateTimeOffset? time = null;

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
                            throw new JsonException($"Unexpected {reader.TokenType}", string.Empty, 0,
                                reader.BytesConsumed);
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
                                case "type":
                                    if (reader.TokenType != JsonTokenType.String)
                                    {
                                        return;
                                    }

                                    type = reader.GetString();
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
                                case "market":
                                    if (reader.TokenType != JsonTokenType.String)
                                    {
                                        return;
                                    }

                                    market = reader.GetString();
                                    break;
                                case "checksum":
                                    if (reader.TokenType == JsonTokenType.Number &&
                                        reader.TryGetInt64(out var checksumValue))
                                    {
                                        checksum = checksumValue;
                                    }

                                    break;
                                case "grouping":
                                    if (reader.TokenType == JsonTokenType.Number &&
                                        reader.TryGetDouble(out var groupingValue))
                                    {
                                        grouping = groupingValue;
                                    }
                                    else
                                    {
                                        return;
                                    }

                                    break;
                                case "time":
                                    switch (reader.TokenType)
                                    {
                                        case JsonTokenType.Number:
                                            if (reader.TryGetInt64(out var timeMs))
                                            {
                                                time = DateTimeOffset.FromUnixTimeMilliseconds(timeMs);
                                            }
                                            else
                                            {
                                                return;
                                            }

                                            break;
                                        default:
                                            if (reader.TryGetDateTimeOffset(out var timeValue))
                                            {
                                                time = timeValue;
                                            }
                                            else
                                            {
                                                return;
                                            }

                                            break;
                                    }

                                    break;
                                case "data":
                                    done = !string.IsNullOrEmpty(type);
                                    return;
                                default:
                                    reader.Skip();
                                    break;
                            }
                        }

                        break;
                }
            }

            done |= reader.BytesConsumed == bytes.Length && !string.IsNullOrEmpty(type);
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


        result = new PreParsedFtxWsMessage(Type: type, Channel: channel, Market: market, Code: code, Msg: msg,
            Checksum: checksum, Grouping: grouping, Time: time);
        return done && !string.IsNullOrWhiteSpace(type);
    }
}