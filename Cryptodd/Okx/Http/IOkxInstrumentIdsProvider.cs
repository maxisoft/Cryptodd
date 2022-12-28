namespace Cryptodd.Okx.Http;

public interface IOkxInstrumentIdsProvider
{
    Task<List<string>> ListInstrumentIds(OkxInstrumentType instrumentType, string? underlying = null,
        string? instrumentFamily = null, string? instrumentId = null, string? expectedState = "live",
        CancellationToken cancellationToken = default);
}