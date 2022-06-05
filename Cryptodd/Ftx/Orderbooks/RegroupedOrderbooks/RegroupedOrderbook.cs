using Cryptodd.Ftx.Models;

namespace Cryptodd.Ftx.Orderbooks.RegroupedOrderbooks;

public struct RegroupedOrderbook
{
    public RegroupedOrderbook() { }

    public PriceSizePair[] Bids { get; set; } = Array.Empty<PriceSizePair>();
    public PriceSizePair[] Asks { get; set; } = Array.Empty<PriceSizePair>();

    public DateTimeOffset Time { get; set; } = DateTimeOffset.UtcNow;

    public string Market { get; set; } = string.Empty;
}