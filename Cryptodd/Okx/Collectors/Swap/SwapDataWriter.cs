using Cryptodd.Mmap;
using Cryptodd.Mmap.Writer;
using Cryptodd.Okx.Models;
using Lamar;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Okx.Collectors.Swap;

public struct SwapDataDoubleSerializerConverter : IDoubleSerializerConverter<
    ValueTuple<OkxHttpOpenInterest, OkxHttpFundingRateWithDate>, SwapData>
{
    public SwapData Convert(in (OkxHttpOpenInterest, OkxHttpFundingRateWithDate) doubleSerializable)
    {
        var (oi, fr) = doubleSerializable;
        var ts = Math.Max(oi.ts, fr.date.ToUnixTimeMilliseconds());
        return new SwapData(
            Timestamp: ts,
            NextFundingTime: fr.nextFundingTime - ts,
            FundingRate: fr.fundingRate,
            NextFundingRate: fr.nextFundingRate,
            OpenInterest: oi.oi,
            OpenInterestInCurrency: oi.oiCcy
        );
    }
}

public class SwapDataWriterOptions : DataWriterOptions
{
    public SwapDataWriterOptions()
    {
        CoalesceExchange("okx");
        Kind = "swap";
    }
}

public class SwapDataWriter : DataWriter<ValueTuple<OkxHttpOpenInterest, OkxHttpFundingRateWithDate>, SwapData, SwapDataDoubleSerializerConverter, SwapDataWriterOptions>
{
    public SwapDataWriter(ILogger logger, IConfiguration configuration, IServiceProvider serviceProvider) : base(logger, configuration, serviceProvider) { }
}