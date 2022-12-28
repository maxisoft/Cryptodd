namespace Cryptodd.Json;

public readonly struct JsonDouble
{
#pragma warning disable CA1051
    public readonly double Value;
#pragma warning restore CA1051
    
    public JsonDouble(double value)
    {
        Value = value;
    }

    public static implicit operator JsonDouble(double value) => new JsonDouble(value);
    
    public static implicit operator double(JsonDouble value) => value.Value;
}