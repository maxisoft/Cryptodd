using System.Text.Json;
using Lamar;
using Maxisoft.Utils.Objects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis.Extensions.Core;
using StackExchange.Redis.Extensions.Core.Abstractions;
using StackExchange.Redis.Extensions.Core.Configuration;
using StackExchange.Redis.Extensions.Core.Implementations;
using StackExchange.Redis.Extensions.System.Text.Json;

namespace CryptoDumper.IoC.Registries.Customs;

public interface IRedisServiceRegistry
{
    
}

public class RedisServiceRegistry : ServiceRegistry, IRedisServiceRegistry
{
    public RedisServiceRegistry()
    {
        ForSingletonOf<ISerializer>().Use(ctx => new SystemTextJsonSerializer(ctx.GetInstance<JsonSerializerOptions>()));
        ForSingletonOf<IRedisServiceRegistry>().Use(this);
        For<RedisConfiguration>().Use(ctx => ctx.GetInstance<IConfiguration>().GetSection("Redis").Get<RedisConfiguration>());
        ForSingletonOf<IRedisClientFactory>().Use(ctx =>
            new RedisClientFactory(ctx.GetAllInstances<RedisConfiguration>(), NullLoggerFactory.Instance, 
                ctx.GetInstance<ISerializer>()));
        For<IRedisClient>().Use(ctx => ctx.GetInstance<IRedisClientFactory>().GetDefaultRedisClient());
        For<IRedisDatabase>().Use(ctx => ctx.GetInstance<IRedisClientFactory>().GetDefaultRedisDatabase());
    }
}