using Cryptodd.Json;
using Maxisoft.Utils.Collections.Lists;

namespace Cryptodd.Okx.Models.RubikStats;

public class OkxHttpRubikLongShortRatio : ArrayList<JsonDouble>
{
    public OkxHttpRubikLongShortRatio() : base(2) { }
    public long Timestamp => (long)this[0];
    public double Ratio => this[1];

    public void Deconstruct(out long timestamp, out double ratio)
    {
        if (Count < 2)
        {
            throw new ArgumentException();
        }

        unsafe
        {
            fixed (JsonDouble* p = AsSpan())
            {
                timestamp = (long)p[0].Value;
                ratio = p[1].Value;
            }
        }
    }
}