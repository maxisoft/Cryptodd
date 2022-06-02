namespace CryptoDumper.Utils;

public static class PriceUtils
{
    public static double Round(double price, double priceIncrement, MidpointRounding rounding = MidpointRounding.AwayFromZero)
    {
        price = Math.Ceiling(price / priceIncrement) * priceIncrement;
        var ndigit = -Math.Log10(2 * priceIncrement);
        if (ndigit > 0)
        {
            ndigit = Math.Ceiling(ndigit);
        }
        else
        {
            ndigit = -Math.Ceiling(-ndigit);
        }

        ndigit = Math.Max(ndigit, 0);

        price = Math.Round(price, (int) ndigit, rounding);
        return price;
    }
}