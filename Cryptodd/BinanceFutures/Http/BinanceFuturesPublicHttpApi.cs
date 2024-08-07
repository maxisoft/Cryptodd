﻿using System.Text.Json;
using System.Text.Json.Nodes;
using Cryptodd.Binance.Http;
using Cryptodd.Binance.Models;
using Cryptodd.BinanceFutures.Http.Options;
using Cryptodd.BinanceFutures.Http.RateLimiter;
using Cryptodd.Ftx.Models.Json;
using Cryptodd.Http;
using Cryptodd.Json;
using Cryptodd.Json.Converters;
using Maxisoft.Utils.Collections.Lists.Specialized;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.BinanceFutures.Http;

public class
    BinanceFuturesPublicHttpApi : BaseBinancePublicHttpApi<BinanceFuturesPublicHttpApiOptions,
        IInternalBinanceFuturesRateLimiter, IBinanceFuturesHttpClientAbstraction>, IBinanceFuturesPublicHttpApi
{
    public BinanceFuturesPublicHttpApi(IBinanceFuturesHttpClientAbstraction client, ILogger logger,
        IConfiguration configuration, IInternalBinanceFuturesRateLimiter internalRateLimiter) : base(client,
        logger, configuration, internalRateLimiter)
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

    Task<BinanceHttpOrderbook> IBinanceHttpOrderbookProvider.GetOrderbook(string symbol, int limit,
        CancellationToken cancellationToken) => GetOrderbook(symbol, limit, cancellationToken: cancellationToken);

    public async Task<PooledList<BinanceHttpKline>> GetKlines(string symbol,
        string interval = "1m",
        long? startTime = null,
        long? endTime = null,
        int limit = IBinanceFuturesPublicHttpApi.DefaultKlineLimit,
        BinanceFuturesPublicHttpApiCallKlinesOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await DotGetKlines(symbol, interval, startTime, endTime, limit, options, cancellationToken)
            .ConfigureAwait(false);


    public async Task<BinanceHttpServerTime> GetServerTime(
        BinanceFuturesPublicHttpApiCallServerTimeOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await DoGetServerTime(options, cancellationToken).ConfigureAwait(false);

    protected override BinanceFuturesPublicHttpApiOptions OptionsValueFactory()
    {
        var res = new BinanceFuturesPublicHttpApiOptions();
        Section.Bind(res);
        return res;
    }

    protected override JsonSerializerOptions CreateJsonSerializerOptions()
    {
        var res = base.CreateJsonSerializerOptions();
        res.Converters.Add(new PooledListConverter<BinancePriceQuantityEntry<double>>
            { DefaultCapacity = IBinanceFuturesPublicHttpApi.MaxOrderbookLimit });
        res.Converters.Add(new PooledListConverter<BinanceHttpKline>()
            { DefaultCapacity = IBinanceFuturesPublicHttpApi.MaxKlineLimit });
        return res;
    }
}