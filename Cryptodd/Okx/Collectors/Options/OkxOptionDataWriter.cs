using Cryptodd.Mmap.Writer;
using Cryptodd.Okx.Models;
using Cryptodd.Okx.Options;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Okx.Collectors.Options;

public class OkxOptionDataWriter : DataWriter<(OkxOptionInstrumentId, OkxHttpOpenInterest, OkxHttpTickerInfo, OkxHttpOptionSummary, OkxHttpInstrumentInfo),
    OkxOptionData, OkxOptionDataDoubleSerializerConverter, OkxOptionDataWriterOptions>
{
    public OkxOptionDataWriter(ILogger logger, IConfiguration configuration, IServiceProvider serviceProvider) : base(logger,
        configuration, serviceProvider) { }
}