namespace Cryptodd.Bitfinex.Models;

[Flags]
public enum RemoteBitfinexWebsocketConfigFlags: long
{
    Timestamp = 32768L,
    SeqAll = 65536L,
    ObChecksum = 131072L,
    BulkUpdates = 536870912L
}