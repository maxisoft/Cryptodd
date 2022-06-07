namespace Cryptodd.Ftx.Models;

public readonly record struct Future(string Name, string? Underlying, string? Description, string Type, DateTimeOffset? Expiry,
    bool Perpetual, bool Expired, bool Enabled, bool PostOnly, double PriceIncrement, double SizeIncrement,
    double? Last, double? Bid, double? Ask, double? Index, double? Mark, double? ImfFactor, double? LowerBound,
    double? UpperBound, string? UnderlyingDescription, string? ExpiryDescription, string? MoveStart,
    double? MarginPrice, double? PositionLimitWeight, string? Group, double? Change1H, double? Change24H,
    double? ChangeBod, double? VolumeUsd24H, double? Volume, double? OpenInterest, double? OpenInterestUsd) { }