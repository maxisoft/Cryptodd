namespace Cryptodd.Bitfinex.Models;

public readonly record struct PreParsedBitfinexWsMessage(string Event, string Channel, long ChanId, string SubId,
    string Symbol,
    string Code, string Msg, bool IsArray, bool IsHearthBeat)
{
    public static bool TryParse(ReadOnlySpan<byte> bytes, out PreParsedBitfinexWsMessage result)
        => PartialPreParsedBitfinexWsMessageParser.TryParse(bytes, out result);
}