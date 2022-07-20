using PetaPoco.SqlKata;

namespace Cryptodd.Databases.Tables;

public abstract class TableSchema : IEquatable<TableSchema>
{
    public string Schema { get; protected internal init; } = string.Empty;

    public string Table => TableNameStandardize.Standardize(GetTableName());

    public bool Equals(TableSchema? other)
    {
        if (ReferenceEquals(null, other))
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Schema == other.Schema;
    }

    protected abstract string GetTableName();

    public abstract ValueTask<string> CreateQuery(CompilerType compilerType, CancellationToken cancellationToken);

    public abstract ValueTask<string> ExistsQuery(CompilerType compilerType, CancellationToken cancellationToken);

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj))
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj.GetType() != GetType())
        {
            return false;
        }

        return Equals((TableSchema)obj);
    }

    public override int GetHashCode() => HashCode.Combine(Schema, Table);

    public static bool operator ==(TableSchema? left, TableSchema? right) => Equals(left, right);

    public static bool operator !=(TableSchema? left, TableSchema? right) => !Equals(left, right);
}