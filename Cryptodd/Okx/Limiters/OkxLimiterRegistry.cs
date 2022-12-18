using System.Collections.Concurrent;
using Cryptodd.IoC;
using Cryptodd.Utils;
using Lamar;
using Maxisoft.Utils.Disposables;
using Microsoft.Extensions.Configuration;

namespace Cryptodd.Okx.Limiters;

[Singleton]
// ReSharper disable once UnusedType.Global
public class OkxLimiterRegistry : IService, IOkxLimiterRegistry, IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly DisposableManager _disposableManager = new();
    private readonly ConcurrentDictionary<string, OkxLimiter> _limiters = new();

    public OkxLimiterRegistry(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public OkxLimiter WebsocketConnectionLimiter =>
        GetOrCreate<IOkxLimiterRegistry.WebsocketConnectionLimiterImpl>("Websocket:Connection");

    public const string WebsocketSubscriptionSectionName = "Websocket:Subscription";

    public OkxLimiter WebsocketSubscriptionLimiter =>
        GetOrCreate<IOkxLimiterRegistry.WebsocketSubscriptionLimiterImpl>(WebsocketSubscriptionSectionName);

    public ReferenceCounterDisposable<OkxLimiter> CreateNewWebsocketSubscriptionLimiter() =>
        new(ValueFactory<IOkxLimiterRegistry.WebsocketSubscriptionLimiterImpl>(WebsocketSubscriptionSectionName,
            withOptionChangeCallback: false));

    private OkxLimiter ValueFactory<T>(string name) where T : OkxLimiter, new() => ValueFactory<T>(name, true);
    private OkxLimiter ValueFactory<T>(string name, bool withOptionChangeCallback) where T : OkxLimiter, new()
    {
        var options = new OkxLimiterOptions();
        var section = _configuration.GetSection("Okx:Limiter").GetSection(name);
        section.Bind(options);
        var res = Create<T>(options);

        if (withOptionChangeCallback)
        {
            static void ChangeCallback(object? o)
            {
                if (o is not Tuple<T, OkxLimiterOptions, IConfigurationSection> tuple)
                {
                    return;
                }

                var (limiter, options, section) = tuple;

                section.Bind(options);
                Update(limiter, options);
            }

            var cb = section.GetReloadToken().RegisterChangeCallback(ChangeCallback,
                new Tuple<T, OkxLimiterOptions, IConfigurationSection>(res, options, section));
            try
            {
                _disposableManager.LinkDisposable(cb);
            }
            catch
            {
                cb.Dispose();
                throw;
            }
        }


        _disposableManager.LinkDisposableAsWeak(res);
        return res;
    }

    private OkxLimiter GetOrCreate<T>(string name) where T : OkxLimiter, new() =>
        _limiters.GetOrAdd(name, ValueFactory<T>);

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
}