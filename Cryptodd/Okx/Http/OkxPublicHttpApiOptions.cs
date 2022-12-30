namespace Cryptodd.Okx.Http;

public class OkxPublicHttpApiOptions
{
    public const string DefaultBaseUrl = "https://aws.okx.com";
    public string BaseUrl { get; set; } = DefaultBaseUrl;

    public string GetInstrumentsUrl { get; set; } = "/api/v5/public/instruments";
    public string GetTickersUrl { get; set; } = "/api/v5/market/tickers";
    
    public string GetOpenInterestUrl { get; set; } = "/api/v5/public/open-interest";
}