using Cryptodd.IO.Mmap.Writer;
using Cryptodd.Okx.Models;
using Cryptodd.Okx.Options;
using Lamar;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Okx.Collectors.Options;

[Singleton]
public class OkxOptionDataWriter : DataWriter<OkxOptionDataContext,
    OkxOptionData, OkxOptionDataDoubleSerializerConverter, OkxOptionDataWriterOptions>
{
    public OkxOptionDataWriter(ILogger logger, IConfiguration configuration, IServiceProvider serviceProvider) : base(
        logger,
        configuration.GetSection("Okx:Collector:Option:Writer"), serviceProvider) { }
}