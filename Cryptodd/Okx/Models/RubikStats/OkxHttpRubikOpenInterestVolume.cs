using Cryptodd.Json;
using Maxisoft.Utils.Collections.Lists;

namespace Cryptodd.Okx.Models.RubikStats;

public class OkxHttpRubikOpenInterestVolume : ArrayList<JsonDouble>
{
    public OkxHttpRubikOpenInterestVolume() : base(3) { }
    public long Timestamp => (long)this[0];
    public double OpenInterest => this[1];
    public double Volume => this[2];

    public void Deconstruct(out long timestamp, out double openInterest, out double volume)
    {
        if (Count < 3)
        {
            throw new ArgumentException();
        }

        unsafe
        {
            fixed (JsonDouble* p = AsSpan())
            {
                timestamp = (long)p[0].Value;
                openInterest = p[1].Value;
                volume = p[2].Value;
            }
        }
    }
}