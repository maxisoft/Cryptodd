using System.Collections.Concurrent;
using System.Text;
using Cryptodd.FileSystem;
using Cryptodd.IoC;
using Lamar;
using Maxisoft.Utils.Collections;
using Maxisoft.Utils.Disposables;
using Microsoft.Extensions.Configuration;

namespace Cryptodd.Pairs;

public interface IPairFilterLoader : IService
{
    ValueTask<IPairFilter> GetPairFilterAsync(string name, CancellationToken cancellationToken = default);
}

[Singleton]
public class PairFilterLoader : IPairFilterLoader, IDisposable
{
    private static readonly char[] Separators = { '/', '.', '+', ':' };
    private readonly IConfiguration _configuration;
    private readonly ConcurrentDictionary<string, PairFilter> _pairFilters = new();
    private readonly IPathResolver _pathResolver;
    private readonly DisposableManager _disposableManager = new DisposableManager();

    public PairFilterLoader(IConfiguration configuration, IPathResolver pathResolver)
    {
        _configuration = configuration;
        _pathResolver = pathResolver;

        var disposable = configuration.GetReloadToken().RegisterChangeCallback(OnConfigurationChange, this);
        try
        {
            _disposableManager.LinkDisposable(disposable);
        }
        catch (Exception)
        {
            disposable.Dispose();
            throw;
        }
    }

    private void OnConfigurationChange(object state)
    {
        if (ReferenceEquals(state, this))
        {
            _pairFilters.Clear();
        }
    }

    public async ValueTask<IPairFilter> GetPairFilterAsync(string name, CancellationToken cancellationToken = default)
    {
        if (_pairFilters.TryGetValue(name, out var res))
        {
            return res;
        }

        res = await LoadPairFilterAsync(name, cancellationToken).ConfigureAwait(false);
        _pairFilters[name] = res;
        return res;
    }

    private async ValueTask<PairFilter> LoadPairFilterAsync(string name, CancellationToken cancellationToken = default)
    {
        var splited = name.Split(Separators, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var paths = splited.ToArrayList(false);
        var res = new PairFilter();

        while (paths.Any())
        {
            var config = paths.Aggregate<string?, dynamic>(_configuration, (current, p) => current.GetSection(p));
            paths.Resize(paths.Count - 1, false);
            if (config is IConfigurationSection section)
            {
                if (!section.Exists())
                {
                    continue;
                }
            }
            else
            {
                continue;
            }

            IList<string>? pairs;
            try
            {
                pairs = section.GetSection("PairFilter").Get<string[]>(null);
            }
            catch (InvalidCastException)
            {
                pairs = null;
            }

            if (pairs is null)
            {
                var pair = section.GetValue("PairFilter", string.Empty);
                if (!string.IsNullOrWhiteSpace(pair))
                {
                    pairs = new[] { pair };
                }
            }

            if (pairs is not null)
            {
                foreach (var pair in pairs)
                {
                    res.AddAll(pair);
                }

                break;
            }

            IList<string>? files;
            try
            {
                files = section.GetValue<List<string>?>("PairFilterFiles", null);
            }
            catch (InvalidCastException)
            {
                files = null;
            }

            if (files is null)
            {
                var file = section.GetValue("PairFilterFile", string.Empty);
                if (!string.IsNullOrWhiteSpace(file))
                {
                    files = new[] { file };
                }
            }

            if (files is null)
            {
                var fileName = Path.Join(paths.ToArray());
                fileName = _pathResolver.Resolve(fileName, new ResolveOption()
                {
                    Namespace = GetType().Namespace!, FileType = "pair filter",
                    IntendedAction = FileIntendedAction.Read
                });
                if (File.Exists(fileName))
                {
                    files = new[] { fileName };
                }
            }

            if (files is not null)
            {
                foreach (var file in files)
                {
                    var content = await File.ReadAllTextAsync(file, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
                    res.AddAll(content);
                }

                break;
            }
        }


        return res;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        _disposableManager.Dispose();
        _disposableManager.UnlinkAll();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}