using Cryptodd.IO.Mmap.Writer;
using Cryptodd.Okx.Collectors.Options;
using Lamar;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Okx.Collectors.RubikStat;
[Singleton]
public class OkxRubikDataWriter : DataWriter<OkxRubikDataContext,
    RubikStatData, OkxRubikDataDoubleSerializerConverter, OkxRubikDataWriterOptions>
{
    public OkxRubikDataWriter(ILogger logger, IConfiguration configuration, IServiceProvider serviceProvider) : base(
        logger,
        configuration.GetSection("Okx:Collector:Rubik:Writer"), serviceProvider) { }
}