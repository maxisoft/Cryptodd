using System.Collections.Concurrent;
using Cryptodd.IoC;
using Cryptodd.Utils;
using Lamar;
using Maxisoft.Utils.Disposables;
using Microsoft.Extensions.Configuration;

namespace Cryptodd.Okx.Limiters;

[Singleton]
// ReSharper disable once UnusedType.Global
// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class OkxLimiterRegistry(IConfiguration configuration) : IService, IOkxLimiterRegistry, IDisposable
{
    private readonly DisposableManager _disposableManager = new();
    private readonly ConcurrentDictionary<string, OkxLimiter> _limiters = new();

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
    
    
    public OkxLimiter GetHttpSubscriptionLimiter<T>(string name, string configName) where T : OkxLimiter, new() => GetOrCreate<T>($"Http:{typeof(T).Name}:{name}", configName);


    private OkxLimiter ValueFactory<T>(string name, string configName, bool withOptionChangeCallback = true) where T : OkxLimiter, new()
    {
        var options = new OkxLimiterOptions();
        var section = configuration.GetSection("Okx:Limiter").GetSection(configName);
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
    
    private OkxLimiter ValueFactory<T>(string name, bool withOptionChangeCallback = true) where T : OkxLimiter, new() => ValueFactory<T>(name, name, withOptionChangeCallback);

    private OkxLimiter GetOrCreate<T>(string name, string configName) where T : OkxLimiter, new() =>
        _limiters.GetOrAdd(name, n => ValueFactory<T>(n, configName));

    private OkxLimiter GetOrCreate<T>(string name) where T : OkxLimiter, new() =>
        GetOrCreate<T>(name, name);

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