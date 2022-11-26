using System.Diagnostics;
using Cryptodd.Bitfinex.WebSockets;
using Cryptodd.FileSystem;
using Cryptodd.IoC;
using PetaPoco.SqlKata;

namespace Cryptodd.Databases.Tables.Bitfinex;

public class OrderbookTableSchema : BaseTableSchema, INoAutoRegister
{
    private readonly string _symbol;
    public int Precision { get; set; } = GroupedOrderBookRequest.DefaultOrderBookLength;
    public int Length { get; set; } = 25;

    public OrderbookTableSchema(string symbol, IResourceResolver resourceResolver) : base(resourceResolver)
    {
        _symbol = symbol;
        Schema = "bitfinex";
        ResourceName = "bitfinex_orderbook";
    }

    protected override string GetTableName()
    {
        return $"bitfinex_ob_{_symbol}_p{Precision}_l{Length}";
    }

    public override async ValueTask<string> CreateQuery(CompilerType compilerType, CancellationToken cancellationToken)
    {
        var result = await base.CreateQuery(compilerType, cancellationToken);
        var original = result;
        result = result.Replace("bitfinex_ob_SYMBOL", Table);
        Debug.Assert(result != original);
        return result;
    }
}

public class OrderbookTableSchemaForSymbolFactory : TableSchemaForSymbolFactory<OrderbookTableSchema>
{
    private readonly IResourceResolver _resourceResolver;

    public OrderbookTableSchemaForSymbolFactory(IResourceResolver resourceResolver)
    {
        _resourceResolver = resourceResolver;
    }

    public override OrderbookTableSchema Resolve(string symbol, CompilerType compilerType) =>
        new(symbol, _resourceResolver);
}