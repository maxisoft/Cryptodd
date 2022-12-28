using System.Text.Json.Serialization;
using Cryptodd.Json;
using Maxisoft.Utils.Collections.Lists.Specialized;
// ReSharper disable InconsistentNaming

namespace Cryptodd.Okx.Models;

public readonly record struct OkxWebSocketArgWithChannelAndInstrumentId(PooledString channel, PooledString instId);

public record struct OkxWebSocketOrderbookData(PooledList<OkxOrderbookEntry> asks, PooledList<OkxOrderbookEntry> bids,
    JsonLong ts, JsonLong checksum) : IDisposable
{
    [JsonIgnore]
    public DateTimeOffset Timestamp => DateTimeOffset.FromUnixTimeMilliseconds(ts.Value);

    public void Dispose()
    {
        asks.Dispose();
        bids.Dispose();
    }
}
public record OkxWebsocketOrderbookResponse(OkxWebSocketArgWithChannelAndInstrumentId arg, PooledString action, OkxWebSocketOrderbookData[] data) : IDisposable
{
    [JsonIgnore]
    public ref OkxWebSocketOrderbookData FirstData => ref data[0];
    
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var sub in data)
            {
                sub.Dispose();
            }
            
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}