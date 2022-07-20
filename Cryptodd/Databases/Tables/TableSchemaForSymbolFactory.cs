using Cryptodd.IoC;
using PetaPoco.SqlKata;

namespace Cryptodd.Databases.Tables;

public abstract class TableSchemaForSymbolFactory<TTableSchema> : IService where TTableSchema : TableSchema
{
    public abstract TTableSchema Resolve(string symbol, CompilerType compilerType);
}