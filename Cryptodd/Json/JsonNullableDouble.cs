using System.Globalization;

namespace Cryptodd.Json;

public readonly struct JsonNullableDouble
{
#pragma warning disable CA1051
    public readonly double? Value;
#pragma warning restore CA1051
    
    public JsonNullableDouble(double? value)
    {
        Value = value;
    }

    public static implicit operator JsonNullableDouble(double value) => new JsonNullableDouble(value);
    
    public static implicit operator double?(JsonNullableDouble value) => value.Value;
    
    public override string ToString() => ToString(NumberFormatInfo.InvariantInfo);
    
    public string ToString(IFormatProvider? provider) => Value?.ToString(provider) ?? "null";
}