﻿/*
 * This file is a plugin for cryptodd.
 * It allow one to change ftx's orderbook parquet filename to a hourly rolling fashion filename.
 * Place it under plugins/RollingParquetFileForFtxOrderbooks folder to activate it.
 * Note that it has to be in a subfolder to be loaded !
 */


#nullable enable

using System;
using System.Globalization;
using System.IO;
using Cryptodd.Ftx.Futures;
using Cryptodd.Ftx.Orderbooks;
using Cryptodd.Ftx.Orderbooks.RegroupedOrderbooks;
using Cryptodd.IO.FileSystem;
using Lamar;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Cryptodd.Plugins.Examples.RollingParquetFileForFtxOrderbooks;

// Register a new plugin by inheriting Cryptodd.BasePlugin 
public class RollingParquetFilePlugin : BasePlugin
{
    // Create a public constructor with a Lamar.IContainer argument
    // ioc (inversion of control) may help one to inject any other service into the plugin
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

    // dependency injection works here too
    public FtxRegroupedOrderbookPathResolver(ILogger logger)
    {
        _logger = logger;
    }

    public string Resolve(string path, in ResolveOption option = default)
    {
        // note that this plugin only handle DefaultFileName.
        // if user provide a custom path we don't handle it
        if (option.FileType != SaveRegroupedOrderbookToParquetHandler.FileType ||
            (path != SaveRegroupedOrderbookToParquetHandler.DefaultFileName &&
             path != SaveOrderbookToParquetHandler.DefaultFileName && 
             path != SaveFuturesStatsToParquetHandler.DefaultFileName &&
             path != Bitfinex.Orderbooks.SaveOrderbookToParquetHandler.DefaultFileName
             ))
        {
            return path;
        }

        // IPluginPathResolver must not throws or it may block the entire process
        try
        {
            return ReplacePath(path);
        }
        catch (Exception e)
        {
            _logger.Error(e, "Unable to replace {Path}", path);
            return path;
        }
    }

    public int Priority { get; } = 0;

    private string ReplacePath(string path)
    {
        var now = DateTimeOffset.UtcNow.ToString("yyyy_MM_dd_HH", DateTimeFormatInfo.InvariantInfo);
        var ext = Path.GetExtension(path);
        var fileName = ((ReadOnlySpan<char>)path)[..^ext.Length];
        var result = $"{fileName}_{now}{ext}";
        result = result.TrimStart('_');
        _logger.Verbose("Changing file {Original} to {File}", path, result);
        return result;
    }
}