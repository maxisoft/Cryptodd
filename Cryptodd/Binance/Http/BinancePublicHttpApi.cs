using System.Text.Json;
using System.Text.Json.Nodes;
using Cryptodd.Binance.Http.Options;
using Cryptodd.Binance.Http.RateLimiter;
using Cryptodd.Binance.Models;
using Cryptodd.Ftx.Models.Json;
using Cryptodd.Http;
using Cryptodd.IoC;
using Cryptodd.Json;
using Cryptodd.Json.Converters;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Binance.Http;

public class BinancePublicHttpApi : BaseBinancePublicHttpApi<BinancePublicHttpApiOptions, IInternalBinanceRateLimiter>,
    IBinancePublicHttpApi, INoAutoRegister
{
    public BinancePublicHttpApi(HttpClient httpClient, ILogger logger, IConfiguration configuration,
        IUriRewriteService uriRewriteService, IInternalBinanceRateLimiter internalRateLimiter) : base(httpClient,
        logger, configuration, uriRewriteService, internalRateLimiter)
    {
        Section = configuration.GetSection("Binance:Http");
        OptionsLazy = new Lazy<BinancePublicHttpApiOptions>(OptionsValueFactory);
        JsonSerializerOptions = new Lazy<JsonSerializerOptions>(CreateJsonSerializerOptions);
    }

    protected override IConfigurationSection Section { get; }

    public async Task<JsonObject> GetExchangeInfoAsync(BinancePublicHttpApiCallExchangeInfoOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await DoGetExchangeInfoAsync(options, cancellationToken);

    public async Task<BinanceHttpOrderbook> GetOrderbook(string symbol,
        int limit = IBinancePublicHttpApi.DefaultOrderbookLimit,
        BinancePublicHttpApiCallOrderBookOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await DotGetOrderbook(symbol, limit, options, cancellationToken);

    public async Task<List<string>> ListSymbols(bool useCache = false, bool checkStatus = false,
        CancellationToken cancellationToken = default) =>
        await DoListSymbols<BinancePublicHttpApiCallExchangeInfoOptions>(useCache,
            checkStatus,
            cancellationToken);

    Task<BinanceHttpOrderbook> IBinanceHttpOrderbookProvider.GetOrderbook(string symbol, int limit,
        CancellationToken cancellationToken) => GetOrderbook(symbol, limit, cancellationToken: cancellationToken);

    protected override BinancePublicHttpApiOptions OptionsValueFactory()
    {
        var res = new BinancePublicHttpApiOptions();
        Section.Bind(res);
        SetupBaseAddress(res.BaseAddress);
        return res;
    }

    protected override JsonSerializerOptions CreateJsonSerializerOptions()
    {
        var res = base.CreateJsonSerializerOptions();
        res.Converters.Add(new PooledListConverter<BinancePriceQuantityEntry<double>>
            { DefaultCapacity = IBinancePublicHttpApi.MaxOrderbookLimit });
        return res;
    }
}