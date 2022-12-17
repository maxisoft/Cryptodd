using System.Collections.Concurrent;
using Cryptodd.IoC;
using Lamar;
using Maxisoft.Utils.Disposables;
using Microsoft.Extensions.Configuration;

namespace Cryptodd.Okx.Limiters;

[Singleton]
public class OkxLimiterRegistry : IService, IOkxLimiterRegistry, IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly DisposableManager _disposableManager = new();
    private readonly ConcurrentDictionary<string, OkxLimiter> _limiters = new();

    public OkxLimiterRegistry(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public OkxLimiter WebsocketConnectionLimiter =>
        GetOrCreate<IOkxLimiterRegistry.WebsocketConnectionLimiterImpl>("Websocket:Connection");

    public OkxLimiter WebsocketSubscriptionLimiter =>
        GetOrCreate<IOkxLimiterRegistry.WebsocketSubscriptionLimiterImpl>("Websocket:Subscription");

    private OkxLimiter GetOrCreate<T>(string name) where T : OkxLimiter, new()
    {
        OkxLimiter ValueFactory(string s)
        {
            var options = new OkxLimiterOptions();
            var section = _configuration.GetSection("Okx:Limiter").GetSection(name);
            section.Bind(options);
            var res = Create<T>(options);

            static void ChangeCallback(object? o)
            {
                if (o is not ValueTuple<T, OkxLimiterOptions, IConfigurationSection> tuple)
                {
                    return;
                }

                var (limiter, options, section) = tuple;

                section.Bind(options);
                Update(limiter, options);
            }

            var cb = section.GetReloadToken().RegisterChangeCallback(ChangeCallback,
                new ValueTuple<T, OkxLimiterOptions, IConfigurationSection>(res, options, section));
            try
            {
                _disposableManager.LinkDisposable(cb);
            }
            catch
            {
                cb.Dispose();
                throw;
            }
            _disposableManager.LinkDisposableAsWeak(res);
            return res;
        }

        return _limiters.GetOrAdd(name, ValueFactory);
    }

    private static void Update<T>(T limiter, OkxLimiterOptions options) where T : OkxLimiter, new()
    {
        limiter.MaxLimit = options.MaxLimit ?? limiter.MaxLimit;
        limiter.Period = options.Period ?? limiter.Period;
        limiter.TickPollingTimer = options.TickPollingTimer ?? limiter.TickPollingTimer;
    }

    private static T Create<T>(OkxLimiterOptions options) where T : OkxLimiter, new()
    {
        var res = new T();

        Update(res, options);
        return res;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposableManager.Dispose();
        }
        _limiters.Clear();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}