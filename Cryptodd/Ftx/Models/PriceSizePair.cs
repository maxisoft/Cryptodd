namespace Cryptodd.Ftx.Models;

public readonly record struct PriceSizePair(double Price, double Size) : IComparable<PriceSizePair>
{
    public int CompareTo(PriceSizePair other)
    {
        var priceComparison = Price.CompareTo(other.Price);
        return priceComparison != 0 ? priceComparison : Size.CompareTo(other.Size);
    }
}