using Cryptodd.Okx.Json;

namespace Cryptodd.Okx.Models;

public class PreParsedOkxWebSocketMessage
{
    public string Event { get; set; } = "";
    public string ArgChannel { get; set; } = "";
    public string ArgInstrumentId { get; set; } = "";
    public string ArgInstrumentType { get; set; } = "";
    public string ArgUnderlying { get; set; } = "";

    public string Action { get; set; } = "";
    public bool HasData { get; set; }
    public long? Code { get; set; }
    public string Message { get; set; } = "";


    public static bool TryParse(ReadOnlySpan<byte> bytes, out PreParsedOkxWebSocketMessage result) =>
        PreParsedOkxWebSocketMessageParser.TryParse(bytes, out result);

    public virtual void CopyTo<T>(T other) where T : PreParsedOkxWebSocketMessage
    {
        other.Event = Event;
        other.ArgChannel = ArgChannel;
        other.ArgInstrumentId = ArgInstrumentId;
        other.ArgInstrumentType = ArgInstrumentType;
        other.ArgUnderlying = ArgUnderlying;
        other.Action = Action;
        other.HasData = HasData;
        other.Code = Code;
        other.Message = Message;
    }
}