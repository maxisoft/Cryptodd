using System.Globalization;
using System.Runtime.CompilerServices;

namespace Cryptodd.Json;

public readonly struct SafeJsonDouble<T> where T:  struct, ISafeJsonDoubleDefaultValue
{
#pragma warning disable CA1051
    public readonly double Value;
#pragma warning restore CA1051
    
    public SafeJsonDouble(double value)
    {
        Value = value;
    }
    
    public SafeJsonDouble()
    {
        Value = new T().GetDefault();
    }

    // ReSharper disable once CompareOfFloatsByEqualityOperator
    public bool IsDefault => Value == new T().GetDefault();

    public static implicit operator SafeJsonDouble<T>(double value) => new (value);
    
    public static implicit operator double(SafeJsonDouble<T> value) => value.Value;
    
    public override string ToString() => ToString(NumberFormatInfo.InvariantInfo);
    
    public string ToString(IFormatProvider? provider) => Value.ToString(provider);
}