namespace Cryptodd.Json;

public readonly struct SafeJsonDoubleDefaultValue : ISafeJsonDoubleDefaultValue
{
    public double GetDefault() => default;
}

public readonly struct SafeJsonDoubleDefaultValueNegativeZero : ISafeJsonDoubleDefaultValue
{
    public double GetDefault() => double.NegativeZero;
}

public readonly struct SafeJsonDoubleDefaultValueMinusOne : ISafeJsonDoubleDefaultValue
{
    public double GetDefault() => -1;
}

public readonly struct SafeJsonDoubleDefaultValueOne : ISafeJsonDoubleDefaultValue
{
    public double GetDefault() => 1;
}

public readonly struct SafeJsonDoubleDefaultInf : ISafeJsonDoubleDefaultValue
{
    public double GetDefault() => double.PositiveInfinity;
}

public readonly struct SafeJsonDoubleDefaultNegInf : ISafeJsonDoubleDefaultValue
{
    public double GetDefault() => double.NegativeInfinity;
}