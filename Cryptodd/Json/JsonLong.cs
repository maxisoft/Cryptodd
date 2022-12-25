namespace Cryptodd.Json;

public readonly struct JsonLong
{
#pragma warning disable CA1051
    public readonly long Value;
#pragma warning restore CA1051
    
    public JsonLong(long value)
    {
        Value = value;
    }

    public static implicit operator JsonLong(long value) => new JsonLong(value);
    
    public static implicit operator long(JsonLong value) => value.Value;
}