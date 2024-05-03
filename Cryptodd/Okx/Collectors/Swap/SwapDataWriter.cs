using Cryptodd.IO.Mmap.Writer;
using Cryptodd.Okx.Models;
using Lamar;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Okx.Collectors.Swap;

[Singleton]
public class SwapDataWriter : DataWriter<ValueTuple<OkxHttpOpenInterest, OkxHttpFundingRate, OkxHttpTickerInfo, OkxHttpMarkPrice>,
    SwapData, SwapDataDoubleSerializerConverter, SwapDataWriterOptions>
{
    public SwapDataWriter(ILogger logger, IConfiguration configuration, IServiceProvider serviceProvider) : base(logger,
        configuration.GetSection("Okx:Swap:Writer"), serviceProvider) { }
}