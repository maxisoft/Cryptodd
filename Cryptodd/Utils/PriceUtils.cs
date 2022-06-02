namespace Cryptodd.Utils;

public static class PriceUtils
{
    public static double Round(double price, double priceIncrement,
        MidpointRounding rounding = MidpointRounding.AwayFromZero)
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

        price = Math.Round(price, (int)ndigit, rounding);
        return price;
    }


    public static float RoundF(float price, float priceIncrement,
        MidpointRounding rounding = MidpointRounding.AwayFromZero)
    {
        price = MathF.Ceiling(price / priceIncrement) * priceIncrement;
        var ndigit = -MathF.Log10(2 * priceIncrement);
        if (ndigit > 0)
        {
            ndigit = MathF.Ceiling(ndigit);
        }
        else
        {
            ndigit = -MathF.Ceiling(-ndigit);
        }

        ndigit = Math.Max(ndigit, 0);

        price = MathF.Round(price, (int)ndigit, rounding);
        return price;
    }
}