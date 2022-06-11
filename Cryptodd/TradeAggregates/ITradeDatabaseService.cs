using System.Data;
using Cryptodd.IoC;
using PetaPoco;

namespace Cryptodd.TradeAggregates;

public interface ITradeDatabaseService : IService
{
    ValueTask<List<long>> GetLatestIds<TDbTransaction>(TDbTransaction transaction, string market, int limit,
        CancellationToken cancellationToken = default) where TDbTransaction: IDbTransaction;

    ValueTask<long> GetLastTime<TDbTransaction>(TDbTransaction transaction, string name,
        bool allowTableCreation = true, CancellationToken cancellationToken = default) where TDbTransaction: IDbTransaction;
    
    ValueTask<long> GetFirstTime<TDbTransaction>(TDbTransaction transaction, string name, CancellationToken cancellationToken,
        bool allowTableCreation = true) where TDbTransaction: IDbTransaction;

    string EscapeMarket(string market);
    string TradeTableName(string market);
}