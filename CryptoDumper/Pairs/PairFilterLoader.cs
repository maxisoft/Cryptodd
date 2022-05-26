using System.Collections.Concurrent;
using System.Text;
using CryptoDumper.FileSystem;
using CryptoDumper.IoC;
using Lamar;
using Maxisoft.Utils.Collections;
using Maxisoft.Utils.Collections.Dictionaries.Specialized;
using Maxisoft.Utils.Collections.Spans;
using Maxisoft.Utils.Empties;
using Microsoft.Extensions.Configuration;

namespace CryptoDumper.Pairs;

public interface IPairFilterLoader : IService
{
    PairFilter GetPairFilter(string name);
}

[Singleton]
public class PairFilterLoader : IPairFilterLoader
{
    private readonly IConfiguration _configuration;
    private ConcurrentDictionary<string, PairFilter> _pairFilters = new ConcurrentDictionary<string, PairFilter>();
    private static readonly char[] Separator = new[] { '/', '.', '+' };
    private readonly IPathResolver _pathResolver;

    public PairFilterLoader(IConfiguration configuration, IPathResolver pathResolver)
    {
        _configuration = configuration;
        _pathResolver = pathResolver;
    }

    public PairFilter GetPairFilter(string name)
    {
        if (_pairFilters.TryGetValue(name, out var res))
        {
            return res;
        }
        res = LoadPairFilter(name);
        _pairFilters[name] = res;
        return res;
    }

    private PairFilter LoadPairFilter(string name)
    {
        var splited = name.Split(Separator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var paths = splited.ToArrayList(copy: false);
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
                    var content = File.ReadAllText(file, Encoding.UTF8);
                    res.AddAll(content);
                }
                break;
            }
        }

        
        return res;
    }
}