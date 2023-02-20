namespace Cryptodd.Okx.Orderbooks;

public class OkxOrderbookCollectorOptions
{
    public int MaxSubscriptionPerWebsocket { get; set; } = 48;
    
    public bool? ReuseConnections { get; set; }
}