using Cryptodd.Okx.Models;

namespace Cryptodd.Okx.Options;

public readonly record struct OkxOptionInstrumentId(
    string Underlying,
    int Year,
    sbyte Month,
    sbyte Day,
    double Price,
    OkxOptionSide Side
)
{
    public static OkxInstrumentType InstrumentType => OkxInstrumentType.Option;

    public DateOnly Date => new(2000 + Year, Month, Day);

    public bool IsPut => Side is OkxOptionSide.Put;

    public bool IsCall => Side is OkxOptionSide.Call;

    public static bool TryParse(string value, out OkxOptionInstrumentId instrumentId) =>
        OkxOptionInstrumentIdParser.TryParse(value, out instrumentId);

    public void Deconstruct(out string underlying, out DateOnly date, out double price, out OkxOptionSide side)
    {
        underlying = Underlying;
        date = Date;
        price = Price;
        side = Side;
    }

    public override string ToString() =>
        $"{Underlying}-{Year:00}{Month:00}{Day:00}-{Price}-{(IsCall ? 'C' : 'P')}";
}