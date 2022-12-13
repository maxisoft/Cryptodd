using System.Data;
using SqlKata.Execution;

namespace Cryptodd.Databases.Postgres;

public readonly struct HijackQueryFactoryConnection : IDisposable
{
    private readonly QueryFactory _queryFactory;
    private readonly IDbConnection? _dbConnection;

    public HijackQueryFactoryConnection(QueryFactory queryFactory, IRentedConnection connection,
        bool disposeUnusedConnection = true)
    {
        _queryFactory = queryFactory;
        _dbConnection = queryFactory.Connection;
        if (disposeUnusedConnection)
        {
            _dbConnection?.Dispose();
        }

        queryFactory.Connection = connection.Connection;
    }


    public void Dispose()
    {
        _queryFactory.Connection = _dbConnection;
    }
}