using System.Buffers;
using System.Data;
using Cryptodd.Pairs;
using Maxisoft.Utils.Objects;
using Npgsql;
using SqlKata.Execution;
using Typin;
using Typin.Attributes;
using Typin.Console;
using Typin.Utilities;

namespace Cryptodd.Cli;

public class ExportToCsvCommandOptions : BaseCommandOptions
{
    public ExportToCsvCommandOptions()
    {
        SetupTimescaleDb = false;
        SetupScheduler = false;
        LoadPlugins = false;
    }
}

[Command("csv", Description = "Export database tables to csv files")]
public class ExportToCsvCommand : BaseCommand<ExportToCsvCommandOptions>, ICommand
{
    public ExportToCsvCommand(): base()
    {
        Options.LoadPlugins = false;
        Options.SetupScheduler = false;
        Options.SetupTimescaleDb = false;
    }
    
    [CommandOption("trades", 't', Description = "Export ftx trades")]
    public bool ExportTrades { get; set; } = false;
    
    [CommandOption("agg", 'g', Description = "Export Aggregated ftx trades")]
    public bool ExportAggregatedTrades { get; set; } = false;
    
    [CommandOption("bitfinex-ob", Description = "Export Bitfinex orderbook")]
    public bool ExportBitfinexOrderbooks { get; set; } = false;

    [CommandOption("timeout", Description = "Operation timeout")]
    public TimeSpan Timeout { get; set; } = TimeSpan.Zero;

    [CommandOption("output", 'o', Description = "Output directory")]
    public string Output { get; set; } = ".";
    
    [CommandOption("limit", 'l', Description = "Limit Number of row")]
    public long Limit { get; set; } = 0;
    
    [CommandOption("filter", Description = "Filter by table name")]
    public List<string> Filters { get; set; } = new List<string>();

    public override async ValueTask ExecuteAsync(IConsole console)
    {
        var outputDirectory = Environment.CurrentDirectory;
        if (!string.IsNullOrEmpty(Output) && Output != ".")
        {
            outputDirectory = Output;
        }

        if (Timeout != TimeSpan.Zero)
        {
            Options.GlobalTimeout = Timeout;
        }

        await PreExecute(console);

        await using var container = GetNewContainer();
        var cancellationToken = container.GetInstance<Boxed<CancellationToken>>().Value;

        using var db = container.GetInstance<QueryFactory>();
        await using var conn = (NpgsqlConnection)db.Connection;

        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(cancellationToken);
        }

        var progressTicker = console.CreateProgressTicker();
        if (ExportTrades)
        {
            await DoExportTrades(db, outputDirectory, cancellationToken);
        }

        if (ExportAggregatedTrades)
        {
            await DoExportAggregatedTrades(db, outputDirectory, Limit, cancellationToken);
        }

        if (ExportBitfinexOrderbooks)
        {
            await DoExportBitfinexOrderbooks(db, outputDirectory, Limit, cancellationToken);
        }
    }

    private async Task CopyCsvStreamToFile(string baseName, TextReader inStream, string outputDirectory,
        CancellationToken cancellationToken)
    {
        baseName = Path.Combine(outputDirectory, baseName);


        static async ValueTask<long> CopyStream(TextReader inStream, TextWriter outStream,
            CancellationToken cancellationToken)
        {
            long res = 0;
            using var buffer = MemoryPool<char>.Shared.Rent(4096);
            var memory = buffer.Memory;
            var read = memory.Length;
            while (read > 0 && !cancellationToken.IsCancellationRequested)
            {
                read = await inStream.ReadBlockAsync(buffer.Memory, cancellationToken);
                await outStream.WriteAsync(memory[..read], cancellationToken);
                res += read;
            }

            return res;
        }

        {
            Logger.Information("Saving to file {File}", baseName);
            await using var outStream = File.CreateText(baseName);
            await CopyStream(inStream, outStream, cancellationToken);
        }
    }

    private async Task DoExportTrades(QueryFactory db, string outputDirectory,
        CancellationToken cancellationToken)
    {
        var conn = (NpgsqlConnection) db.Connection;
        var tableSizes = (await db.Query("table_sizes2")
            .Where("schema", "=", "ftx")
            .WhereLike("name", "ftx_trade_%_%")
            .WhereNotLike("name", "ftx_trade_agg_%_%")
            .OrderByRaw("pg_size_bytes(total_bytes) DESC")
            .GetAsync(cancellationToken: cancellationToken)).ToList();

        var progress = 0;
        foreach (var tableSize in tableSizes)
        {
            if (tableSize is null)
            {
                continue;
            }

            using var inStream = await conn.BeginTextExportAsync(
                $"COPY (SELECT * FROM \"{tableSize.schema}\".\"{tableSize.name}\") TO STDOUT WITH CSV HEADER ENCODING 'UTF8'",
                cancellationToken);
            var baseName = $"{tableSize.schema}_{tableSize.name}.csv";
            await CopyCsvStreamToFile(baseName, inStream, outputDirectory, cancellationToken);

            progress += 1;
        }
    }

    private async Task DoExportAggregatedTrades(QueryFactory db, string outputDirectory, long limit,
        CancellationToken cancellationToken)
    {
        var conn = (NpgsqlConnection) db.Connection;
        var tableSizes = (await db.Query("pg_tables")
            .Select("schemaname AS schema", "tablename as name")
            .Where("schemaname", "=", "ftx")
            .WhereLike("tablename", "ftx_trade_agg_%_%")
            .GetAsync(cancellationToken: cancellationToken)).ToList();

        var progress = 0;
        var orderByQuery = " ORDER BY \"time\"";
        var limitQuery = "";
        if (limit > 0)
        {
            orderByQuery += " DESC";
            limitQuery = $" LIMIT {limit}";
        }

        var filter = new PairFilter();
        foreach (var filter1 in Filters)
        {
            filter.AddAll(filter1);
        }
        foreach (var tableSize in tableSizes.Where(tableSize => tableSize is not null && filter.Match(tableSize.name)))
        {
            using var inStream = await conn.BeginTextExportAsync(
                $"COPY (SELECT * FROM \"{tableSize.schema}\".\"{tableSize.name}\" {orderByQuery} {limitQuery}) TO STDOUT WITH CSV HEADER ENCODING 'UTF8'",
                cancellationToken);
            var baseName = $"{tableSize.schema}_{tableSize.name}.csv";
            await CopyCsvStreamToFile(baseName, inStream, outputDirectory, cancellationToken);

            progress += 1;
        }
    }
    
    private async Task DoExportBitfinexOrderbooks(QueryFactory db, string outputDirectory, long limit,
        CancellationToken cancellationToken)
    {
        var conn = (NpgsqlConnection) db.Connection;
        var tableSizes = (await db.Query("pg_tables")
            .Select("schemaname AS schema", "tablename as name")
            .Where("schemaname", "=", "bitfinex")
            .WhereLike("tablename", "bitfinex_ob_%")
            .GetAsync(cancellationToken: cancellationToken)).ToList();

        var progress = 0;
        var orderByQuery = " ORDER BY \"time\"";
        var limitQuery = "";
        if (limit > 0)
        {
            orderByQuery += " DESC";
            limitQuery = $" LIMIT {limit}";
        }

        var filter = new PairFilter();
        foreach (var filter1 in Filters)
        {
            filter.AddAll(filter1);
        }
        foreach (var tableSize in tableSizes.Where(tableSize => tableSize is not null && filter.Match(tableSize.name)))
        {
            using var inStream = await conn.BeginTextExportAsync(
                $"COPY (SELECT * FROM \"{tableSize.schema}\".\"{tableSize.name}\" {orderByQuery} {limitQuery}) TO STDOUT WITH CSV HEADER ENCODING 'UTF8'",
                cancellationToken);
            var baseName = $"{tableSize.schema}_{tableSize.name}.csv";
            await CopyCsvStreamToFile(baseName, inStream, outputDirectory, cancellationToken);

            progress += 1;
        }
    }
}