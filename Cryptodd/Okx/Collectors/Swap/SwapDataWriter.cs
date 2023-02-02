﻿using Cryptodd.Mmap.Writer;
using Cryptodd.Okx.Models;
using Lamar;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Okx.Collectors.Swap;

public class SwapDataWriter : DataWriter<ValueTuple<OkxHttpOpenInterest, OkxHttpFundingRateWithDate, OkxHttpTickerInfo, OkxHttpMarkPrice>,
    SwapData, SwapDataDoubleSerializerConverter, SwapDataWriterOptions>
{
    public SwapDataWriter(ILogger logger, IConfiguration configuration, IServiceProvider serviceProvider) : base(logger,
        configuration, serviceProvider) { }
}