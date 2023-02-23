using System.Text.Json;
using System.Text.Json.Nodes;
using Cryptodd.Binance.Http.Options;
using Cryptodd.Binance.Http.RateLimiter;
using Cryptodd.Binance.Json;
using Cryptodd.Binance.Models;
using Cryptodd.Ftx.Models.Json;
using Cryptodd.Http;
using Cryptodd.IoC;
using Cryptodd.Json;
using Cryptodd.Json.Converters;
using Maxisoft.Utils.Collections.Lists.Specialized;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Binance.Http;

public class BinancePublicHttpApi : BaseBinancePublicHttpApi<BinancePublicHttpApiOptions, IInternalBinanceRateLimiter, IBinanceHttpClientAbstraction>,
    IBinancePublicHttpApi, INoAutoRegister
{
    public BinancePublicHttpApi(IBinanceHttpClientAbstraction client, ILogger logger, IConfiguration configuration, IInternalBinanceRateLimiter internalRateLimiter) : base(client,
        logger, configuration, internalRateLimiter)
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
        await DotGetOrderbook(symbol, limit, options, cancellationToken).ConfigureAwait(false);
    
    public async Task<PooledList<BinanceHttpKline>> GetKlines(string symbol,
        string interval = "1m",
        long? startTime = null,
        long? endTime = null,
        int limit = IBinancePublicHttpApi.DefaultKlineLimit,
        BinancePublicHttpApiCallKlinesOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await DotGetKlines(symbol, interval, startTime, endTime, limit, options, cancellationToken).ConfigureAwait(false);

    public async Task<List<string>> ListSymbols(bool useCache = false, bool checkStatus = false,
        CancellationToken cancellationToken = default) =>
        await DoListSymbols<BinancePublicHttpApiCallExchangeInfoOptions>(useCache,
            checkStatus,
            cancellationToken).ConfigureAwait(false);

    Task<BinanceHttpOrderbook> IBinanceHttpOrderbookProvider.GetOrderbook(string symbol, int limit,
        CancellationToken cancellationToken) => GetOrderbook(symbol, limit, cancellationToken: cancellationToken);

    protected override BinancePublicHttpApiOptions OptionsValueFactory()
    {
        var res = new BinancePublicHttpApiOptions();
        Section.Bind(res);
        return res;
    }

    protected override JsonSerializerOptions CreateJsonSerializerOptions()
    {
        var res = base.CreateJsonSerializerOptions();
        res.Converters.Add(new PooledListConverter<BinancePriceQuantityEntry<double>>
            { DefaultCapacity = IBinancePublicHttpApi.MaxOrderbookLimit });
        res.Converters.Add(new PooledListConverter<BinanceHttpKline>() { DefaultCapacity = IBinancePublicHttpApi.MaxKlineLimit });
        return res;
    }
}