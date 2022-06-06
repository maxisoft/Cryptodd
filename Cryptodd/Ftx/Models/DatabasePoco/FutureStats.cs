using PetaPoco;

namespace Cryptodd.Ftx.Models.DatabasePoco;

[ExplicitColumns]
[TableName(Naming.TableName)]
[PrimaryKey("id")]
public class FutureStats
{
    [Column("id")] public long Id { get; set; }

    [Column("market_hash")] public long MarketHash { get; set; }

    [Column("time")] public long Time { get; set; }

    [Column("open_interest")] public double OpenInterest { get; set; }

    [Column("open_interest_usd")] public double OpenInterestUsd { get; set; }

    [Column("next_funding_rate")] public float NextFundingRate { get; set; } = 0.0f;

    [Column("spread")] public float Spread { get; set; } = 0.0f;
    [Column("mark")] public float Mark { get; set; }

    public static class Naming
    {
        public const string TableName = "ftx_futures_stats";
    }
}