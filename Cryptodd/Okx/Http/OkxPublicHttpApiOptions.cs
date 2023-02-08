namespace Cryptodd.Okx.Http;

public class OkxPublicHttpApiOptions
{
    public const string DefaultBaseUrl = "https://aws.okx.com";
    public string BaseUrl { get; set; } = DefaultBaseUrl;

    public string GetInstrumentsUrl { get; set; } = "/api/v5/public/instruments";
    public string GetTickersUrl { get; set; } = "/api/v5/market/tickers";

    public string GetMarkPricesUrl { get; set; } = "/api/v5/public/mark-price";

    public string GetOpenInterestUrl { get; set; } = "/api/v5/public/open-interest";
    public string GetFundingRateUrl { get; set; } = "/api/v5/public/funding-rate";

    public string GetOptionMarketDataUrl { get; set; } = "/api/v5/public/opt-summary";

    public string GetTakerVolumeUrl { get; set; } = "/api/v5/rubik/stat/taker-volume";
    public string GetMarginLendingRatioUrl { get; set; } = "/api/v5/rubik/stat/margin/loan-ratio";
    public string GetLongShortRatioUrl { get; set; } = "/api/v5/rubik/stat/contracts/long-short-account-ratio";
    
    public string GetSupportCoinUrl { get; set; } = "/api/v5/rubik/stat/trading-data/support-coin";
    
    public string GetContractsOpenInterestAndVolumeUrl { get; set; } = "/api/v5/rubik/stat/contracts/open-interest-volume";
}