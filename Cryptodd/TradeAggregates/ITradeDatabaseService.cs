using Cryptodd.IoC;
using PetaPoco;

namespace Cryptodd.TradeAggregates;

public interface ITradeDatabaseService : IService
{
    ValueTask<List<long>> GetLatestIds(string market, int limit, IDatabase? database,
        CancellationToken cancellationToken = default);

    ValueTask<long> GetSavedTime(string name, CancellationToken cancellationToken,
        bool allowTableCreation = true);

    string EscapeMarket(string market);
    string TradeTableName(string market);
}