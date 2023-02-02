using System.Reflection;
using Cryptodd.IoC;

namespace Cryptodd.FileSystem;

public interface IResourceResolver : IService
{
    ValueTask<string> GetResource(string path, CancellationToken cancellationToken);
}

// ReSharper disable once UnusedType.Global
public class ResourceResolver: IResourceResolver
{
    public async ValueTask<string> GetResource(string path, CancellationToken cancellationToken)
    {
        var asm = Assembly.GetExecutingAssembly();
        var resource = $"{asm.GetName().Name}.{path}";
        await using var stream = asm.GetManifestResourceStream(resource);
        if (stream is not null)
        {
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }

        throw new FileNotFoundException("Unable to load resource", resource);
    }
}