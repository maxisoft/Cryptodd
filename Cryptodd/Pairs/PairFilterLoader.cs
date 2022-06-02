using System.Collections.Concurrent;
using System.Text;
using Cryptodd.FileSystem;
using Cryptodd.IoC;
using Lamar;
using Maxisoft.Utils.Collections;
using Microsoft.Extensions.Configuration;

namespace Cryptodd.Pairs;

public interface IPairFilterLoader : IService
{
    ValueTask<IPairFilter> GetPairFilterAsync(string name, CancellationToken cancellationToken = default);
}

[Singleton]
public class PairFilterLoader : IPairFilterLoader
{
    private static readonly char[] Separator = { '/', '.', '+' };
    private readonly IConfiguration _configuration;
    private readonly ConcurrentDictionary<string, PairFilter> _pairFilters = new();
    private readonly IPathResolver _pathResolver;

    public PairFilterLoader(IConfiguration configuration, IPathResolver pathResolver)
    {
        _configuration = configuration;
        _pathResolver = pathResolver;
    }

    public async ValueTask<IPairFilter> GetPairFilterAsync(string name, CancellationToken cancellationToken = default)
    {
        if (_pairFilters.TryGetValue(name, out var res))
        {
            return res;
        }

        res = await LoadPairFilterAsync(name, cancellationToken);
        _pairFilters[name] = res;
        return res;
    }

    private async ValueTask<PairFilter> LoadPairFilterAsync(string name, CancellationToken cancellationToken = default)
    {
        var splited = name.Split(Separator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
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
                pairs = section.GetValue<List<string>?>("PairFilter", null);
            }
            catch (InvalidCastException)
            {
                pairs = null;
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
                fileName = _pathResolver.Resolve(fileName);
                if (File.Exists(fileName))
                {
                    files = new[] { fileName };
                }
            }

            if (files is not null)
            {
                foreach (var file in files)
                {
                    var content = await File.ReadAllTextAsync(file, Encoding.UTF8, cancellationToken);
                    res.AddAll(content);
                }

                break;
            }
        }


        return res;
    }
}