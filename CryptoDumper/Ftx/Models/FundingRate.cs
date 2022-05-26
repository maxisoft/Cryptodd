namespace CryptoDumper.Ftx.Models;

public readonly record struct FundingRate(string Future, double? Rate, DateTimeOffset Time);