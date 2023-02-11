using Cryptodd.IO.Mmap.Writer;
using Cryptodd.Okx.Collectors.Options;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Okx.Collectors.RubikStat;

public class OkxRubikDataWriter : DataWriter<OkxRubikDataContext,
    RubikStatData, OkxRubikDataDoubleSerializerConverter, OkxRubikDataWriterOptions>
{
    public OkxRubikDataWriter(ILogger logger, IConfiguration configuration, IServiceProvider serviceProvider) : base(
        logger,
        configuration, serviceProvider) { }
}