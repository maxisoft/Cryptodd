/*
 * This file is a plugin for cryptodd.
 * It allow one to change ftx's orderbook parquet filename to a hourly rolling fashion filename.
 * Place it under plugins/RollingParquetFileForFtxOrderbooks folder to activate it.
 * Note that it has to be in a subfolder to be loaded !
 */

using System;
using System.Globalization;
using Cryptodd.FileSystem;
using Cryptodd.Ftx.RegroupedOrderbooks;
using Lamar;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Cryptodd.Plugins.Examples.RollingParquetFileForFtxOrderbooks;

// Register a new plugin by inheriting Cryptodd.BasePlugin
public class RollingParquetFilePlugin : BasePlugin
{
    // Create a public constructor with a Lamar.IContainer argument
    // Note that ioc (inversion of control) may help one to inject any other service into the plugin
    public RollingParquetFilePlugin(IContainer container) : base(container)
    {
        // reconfigure ioc to use our PluginPathResolver
        container.Configure(collection =>
        {
            collection.AddTransient<IPluginPathResolver, FtxRegroupedOrderbookPathResolver>();
        });
    }
}

public class FtxRegroupedOrderbookPathResolver : IPluginPathResolver
{
    private readonly ILogger _logger;

    // Note that dependency injection works here too
    public FtxRegroupedOrderbookPathResolver(ILogger logger)
    {
        _logger = logger;
    }

    public string Resolve(string path, in ResolveOption option = default)
    {
        // note that this plugin only handle DefaultFileName.
        // if user provide a custom path we don't handle it
        if (path != SaveRegroupedOrderbookToParquetHandler.DefaultFileName ||
            option.FileType != SaveRegroupedOrderbookToParquetHandler.FileType)
        {
            return path;
        }

        var now = DateTimeOffset.UtcNow.ToString("yyyy_MM_dd_hh", DateTimeFormatInfo.InvariantInfo);
        var result = $"ftx_regrouped_orderbook_{now}.parquet";
        _logger.Verbose("Changing file {Original} to {File}", path, result);
        return result;
    }

    public int Priority { get; } = 0;
}