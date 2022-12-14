﻿using System.Text.Json;
using System.Text.Json.Nodes;
using Cryptodd.Binance.Http;
using Cryptodd.Binance.Models;
using Cryptodd.BinanceFutures.Http.Options;
using Cryptodd.BinanceFutures.Http.RateLimiter;
using Cryptodd.Ftx.Models.Json;
using Cryptodd.Http;
using Microsoft.Extensions.Configuration;

namespace Cryptodd.BinanceFutures.Http;

public class
    BinanceFuturesPublicHttpApi : BaseBinancePublicHttpApi<BinanceFuturesPublicHttpApiOptions,
        IInternalBinanceFuturesRateLimiter>, IBinanceFuturesPublicHttpApi
{
    public BinanceFuturesPublicHttpApi(HttpClient httpClient, IConfiguration configuration,
        IUriRewriteService uriRewriteService, IInternalBinanceFuturesRateLimiter internalRateLimiter) : base(httpClient,
        configuration, uriRewriteService, internalRateLimiter)
    {
        Section = configuration.GetSection("BinanceFutures:Http");
        OptionsLazy = new Lazy<BinanceFuturesPublicHttpApiOptions>(OptionsValueFactory);
        JsonSerializerOptions = new Lazy<JsonSerializerOptions>(CreateJsonSerializerOptions);
    }

    protected override IConfigurationSection Section { get; }

    public new IBinanceFuturesRateLimiter RateLimiter => InternalRateLimiter;

    public async Task<JsonObject> GetExchangeInfoAsync(
        BinanceFuturesPublicHttpApiCallExchangeInfoOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await DoGetExchangeInfoAsync(options, cancellationToken);

    public async Task<BinanceHttpOrderbook> GetOrderbook(string symbol,
        int limit = IBinanceFuturesPublicHttpApi.DefaultOrderbookLimit,
        BinanceFuturesPublicHttpApiCallOrderBookOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await DotGetOrderbook(symbol, limit, options, cancellationToken);

    public async Task<List<string>> ListSymbols(bool useCache = false, bool checkStatus = false,
        CancellationToken cancellationToken = default) =>
        await DoListSymbols<BinanceFuturesPublicHttpApiCallExchangeInfoOptions>(useCache,
            checkStatus, cancellationToken);

    protected override BinanceFuturesPublicHttpApiOptions OptionsValueFactory()
    {
        var res = new BinanceFuturesPublicHttpApiOptions();
        Section.Bind(res);
        SetupBaseAddress(res.BaseAddress);
        return res;
    }


    protected override JsonSerializerOptions CreateJsonSerializerOptions()
    {
        var res = base.CreateJsonSerializerOptions();
        res.Converters.Add(new PooledListConverter<BinancePriceQuantityEntry<double>>
            { DefaultCapacity = IBinanceFuturesPublicHttpApi.MaxOrderbookLimit });
        return res;
    }
}