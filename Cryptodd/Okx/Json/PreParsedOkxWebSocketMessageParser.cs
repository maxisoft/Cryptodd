using System.Text.Json;
using Cryptodd.Json;
using Cryptodd.Okx.Models;

namespace Cryptodd.Okx.Json;

internal static class PreParsedOkxWebSocketMessageParser
{
    internal static readonly StringPool StringPool = new(16 << 10);

    static PreParsedOkxWebSocketMessageParser()
    {
        StringPool.Cache("SPOT");
        StringPool.Cache("MARGIN");
        StringPool.Cache("SWAP");
        StringPool.Cache("FUTURES");
        StringPool.Cache("OPTION");
        StringPool.Cache("ANY");
        StringPool.Cache("subscribe");
        StringPool.Cache("unsubscribe");
        StringPool.Cache("error");
        StringPool.Cache("books");
        StringPool.Cache("books5");
        StringPool.Cache("bbo-tbt");
        StringPool.Cache("books-l2-tbt");
        StringPool.Cache("books50-l2-tbt");
    }

    private static readonly JsonReaderOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false
    };

    public static bool TryParse(ReadOnlySpan<byte> bytes, out PreParsedOkxWebSocketMessage result)
    {
        string eventName = "", channel = "", action = "";
        string msg = "", instId = "", tmp = "", uly = "", instType = "";

        bool? hasData = null;
        long? code = null;

        var done = false;

        #region ConsumeJson

        // ReSharper disable once VariableHidesOuterVariable
        void ConsumeJson(ReadOnlySpan<byte> bytes)
        {
            var reader = new Utf8JsonReader(bytes, Options);
            var propertyBytes = ReadOnlySpan<byte>.Empty;
            var parsingArg = false;

            var startObjectCounter = 0;

            while (reader.Read() && !done)
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        startObjectCounter++;
                        if (startObjectCounter > 1)
                        {
                            if (startObjectCounter == 2 && propertyBytes.SequenceEqual("arg"u8))
                            {
                                parsingArg = true;
                            }
                            else
                            {
                                reader.Skip();
                            }
                        }

                        break;
                    case JsonTokenType.EndObject:
                        startObjectCounter--;
                        if (parsingArg && startObjectCounter < 2)
                        {
                            parsingArg = false;
                        }

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
                            propertyBytes = reader.ValueSpan;
                            if (propertyBytes.SequenceEqual("arg"u8))
                            {
                                continue;
                            }
                            if (!reader.Read())
                            {
                                return;
                            }

                            if (reader.TokenType == JsonTokenType.String)
                            {
                                if (propertyBytes.SequenceEqual("event"u8))
                                {
                                    if (!StringPool.TryGetString(reader.ValueSpan, out eventName))
                                    {
                                        eventName = reader.GetString() ?? throw new JsonException(
                                            "unable to read stream string", string.Empty, 0,
                                            reader.BytesConsumed);
                                    }

                                    done |= !string.IsNullOrWhiteSpace(channel) && hasData is true;
                                    break;
                                }

                                if (propertyBytes.SequenceEqual("action"u8))
                                {
                                    if (!StringPool.TryGetString(reader.ValueSpan, out action))
                                    {
                                        action = reader.GetString() ?? throw new JsonException(
                                            "unable to read stream string", string.Empty, 0,
                                            reader.BytesConsumed);
                                    }

                                    done |= !string.IsNullOrWhiteSpace(channel) && hasData is true;
                                    break;
                                }
                                else if (propertyBytes.SequenceEqual("code"u8))
                                {
                                    if (!StringPool.TryGetString(reader.ValueSpan, out tmp))
                                    {
                                        tmp = reader.GetString() ?? throw new JsonException(
                                            "unable to read stream string", string.Empty, 0,
                                            reader.BytesConsumed);
                                    }

                                    if (long.TryParse(tmp, out var codeLong))
                                    {
                                        code = codeLong;
                                        done |= !string.IsNullOrWhiteSpace(eventName) &&
                                                !string.IsNullOrWhiteSpace(msg);
                                    }

                                    break;
                                }
                                else if (propertyBytes.SequenceEqual("msg"u8))
                                {
                                    msg = reader.GetString() ?? throw new JsonException(
                                        "unable to read stream string", string.Empty, 0,
                                        reader.BytesConsumed);
                                    done |= !string.IsNullOrWhiteSpace(eventName) && code is not null;
                                    break;
                                }
                            }
                            else if (reader.TokenType == JsonTokenType.Number)
                            {
                                if (propertyBytes.SequenceEqual("code"u8))
                                {
                                    if (!reader.TryGetInt64(out var codeLong))
                                    {
                                        throw new JsonException(
                                            "unable to read code, a number property", string.Empty, 0,
                                            reader.BytesConsumed);
                                    }

                                    code = codeLong;
                                    done |= !string.IsNullOrWhiteSpace(eventName) && !string.IsNullOrWhiteSpace(msg);
                                    break;
                                }
                            }
                        }
                        else if (startObjectCounter == 2 && parsingArg)
                        {
                            propertyBytes = reader.ValueSpan;
                            if (!reader.Read())
                            {
                                return;
                            }

                            if (reader.TokenType == JsonTokenType.String)
                            {
                                if (propertyBytes.SequenceEqual("channel"u8))
                                {
                                    if (!StringPool.TryGetString(reader.ValueSpan, out channel))
                                    {
                                        channel = reader.GetString() ?? throw new JsonException(
                                            "unable to read stream string", string.Empty, 0,
                                            reader.BytesConsumed);
                                    }

                                    break;
                                }
                                else if (propertyBytes.SequenceEqual("instId"u8))
                                {
                                    if (!StringPool.TryGetString(reader.ValueSpan, out instId))
                                    {
                                        instId = reader.GetString() ?? throw new JsonException(
                                            "unable to read stream string", string.Empty, 0,
                                            reader.BytesConsumed);
                                    }

                                    break;
                                }
                                else if (propertyBytes.SequenceEqual("instType"u8))
                                {
                                    if (!StringPool.TryGetString(reader.ValueSpan, out instType))
                                    {
                                        instType = reader.GetString() ?? throw new JsonException(
                                            "unable to read stream string", string.Empty, 0,
                                            reader.BytesConsumed);
                                    }

                                    break;
                                }
                                else if (propertyBytes.SequenceEqual("uly"u8))
                                {
                                    if (!StringPool.TryGetString(reader.ValueSpan, out uly))
                                    {
                                        uly = reader.GetString() ?? throw new JsonException(
                                            "unable to read stream string", string.Empty, 0,
                                            reader.BytesConsumed);
                                    }

                                    break;
                                }
                            }
                        }

                        if (startObjectCounter == 1 && hasData is not true && propertyBytes.SequenceEqual("data"u8))
                        {
                            hasData = true;
                            done |= !string.IsNullOrWhiteSpace(channel) || !string.IsNullOrWhiteSpace(action);
                        }

                        reader.Skip();
                        break;
                }
            }

            done &= reader.BytesConsumed > 0;
        }

        #endregion

        try
        {
            ConsumeJson(bytes);

            if (!done)
            {
                done = eventName switch
                {
                    "subscribe" or "unsubscribe" => !string.IsNullOrWhiteSpace(channel),
                    "error" => code.HasValue || !string.IsNullOrWhiteSpace(msg),
                    _ => done
                };
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException)
        {
            done = false;
        }


        hasData ??= bytes.IndexOf("\"data\":"u8) >= 0;

        if (!done && hasData is true)
        {
            done = !string.IsNullOrWhiteSpace(channel) || !string.IsNullOrWhiteSpace(action);
        }

        result = new PreParsedOkxWebSocketMessage()
        {
            ArgInstrumentId = instId, Action = action, ArgChannel = channel,
            ArgUnderlying = uly, Code = code, Event = eventName,
            HasData = hasData ?? false, Message = msg, ArgInstrumentType = instType
        };
        return done;
    }
}