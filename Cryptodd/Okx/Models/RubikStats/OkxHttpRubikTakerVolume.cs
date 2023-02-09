using Cryptodd.Json;
using Maxisoft.Utils.Collections.Lists;

namespace Cryptodd.Okx.Models.RubikStats;

public class OkxHttpRubikTakerVolume : ArrayList<JsonDouble>
{
    public OkxHttpRubikTakerVolume() : base(3) { }
    public long Timestamp => (long)this[0];
    public double SellVolume => this[1];
    public double BuyVolume => this[2];

    public void Deconstruct(out long timestamp, out double sellVolume, out double buyVolume)
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
                sellVolume = p[1].Value;
                buyVolume = p[2].Value;
            }
        }
    }
}