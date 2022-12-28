using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Cryptodd.Tests.TestingHelpers.Logging;

// ReSharper disable once ClassNeverInstantiated.Global
public class RealLogger : ILogger
{
    public ILogger Logger { get; set; } = new LoggerConfiguration().CreateLogger();
    
    public virtual void Write(LogEvent logEvent)
    {
        Logger.Write(logEvent);
    }
}