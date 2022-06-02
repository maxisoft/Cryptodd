using System.Text.Json.Serialization;

namespace CryptoDumper.Ftx.Models;

public class GroupedOrderbookDetails
{
    public string Type { get; set; } = string.Empty;
    public string Channel { get; set; }  = string.Empty;
    public string Market { get; set; }  = string.Empty;
    [JsonIgnore] public string Code { get; set; }  = string.Empty;
    [JsonIgnore] public string Msg { get; set; }  = string.Empty;

    public long Checksum { get; set; }
    public double Grouping { get; set; }
    public DateTimeOffset Time { get; set; } = DateTimeOffset.UtcNow;

    public GroupedOrderbook Data { get; set; } = GroupedOrderbook.Empty;

    public GroupedOrderBookRequest ToRequest()
    {
        return new GroupedOrderBookRequest(Market, Grouping);
    }
}