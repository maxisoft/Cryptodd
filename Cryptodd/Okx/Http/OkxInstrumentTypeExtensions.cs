namespace Cryptodd.Okx.Http;

public static class OkxInstrumentTypeExtensions
{
    public static string ToHttpString(this OkxInstrumentType instrumentType) =>
        Enum.GetName(instrumentType)?.ToUpperInvariant() ??
        throw new ArgumentOutOfRangeException(nameof(instrumentType));
}