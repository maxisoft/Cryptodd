using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Maxisoft.Plugins.Loader
{
    public interface IAssemblyReferenceCollector
    {
        IEnumerable<MetadataReference> CollectMetadataReferences(Assembly assembly);
    }

    public class AssemblyReferenceCollector : IAssemblyReferenceCollector
    {
        private static IEnumerable<MetadataReference> CollectMetadataReferences(Assembly assembly, ISet<Assembly> dejaVu)
        {
            if (!dejaVu.Any() && assembly != typeof(object).Assembly)
            {
                foreach (var reference in CollectMetadataReferences(typeof(object).Assembly, dejaVu))
                {
                    yield return reference;
                }
            }
            if (dejaVu.Add(assembly))
            {
                yield return MetadataReference.CreateFromFile(assembly.Location);
                foreach (var assemblyName in assembly.GetReferencedAssemblies())
                {
                    var ass = Assembly.Load(assemblyName);
                    if (dejaVu.Contains(ass))
                    {
                        continue;
                    }

                    foreach (var reference in CollectMetadataReferences(ass, dejaVu))
                    {
                        yield return reference;
                    }
                    Debug.Assert(dejaVu.Contains(ass));
                }
            }
            Debug.Assert(dejaVu.Contains(typeof(object).Assembly));
        }

        public IEnumerable<MetadataReference> CollectMetadataReferences(Assembly assembly)
        {
            return CollectMetadataReferences(assembly, new HashSet<Assembly>());
        }
    }
}