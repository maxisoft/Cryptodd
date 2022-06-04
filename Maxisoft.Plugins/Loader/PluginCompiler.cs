using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

[assembly: InternalsVisibleTo("Maxisoft.Plugins.Tests")]

namespace Maxisoft.Plugins.Loader
{
    public interface IPluginCompiler
    {
        Task<CompilerResult> CompileAsync(string path, Assembly? parent = null,
            CancellationToken cancellationToken = default);
    }

    public class PluginCompiler : IPluginCompiler
    {
        public PluginCompiler(IAssemblyReferenceCollector assemblyReferenceCollector)
        {
            _assemblyReferenceCollector = assemblyReferenceCollector ??
                                          throw new ArgumentNullException(nameof(assemblyReferenceCollector));
        }

        private readonly IAssemblyReferenceCollector _assemblyReferenceCollector;

        /// <inheritdoc />
        public async Task<CompilerResult> CompileAsync(string path, Assembly? parent = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));
            var context = CreateContextFromPath(path);
            context.RootAssembly = parent ?? GetType().Assembly;
            var linkedAssemblies = _assemblyReferenceCollector.CollectMetadataReferences(context.RootAssembly).ToHashSet();
            context.LinkedAssemblies = linkedAssemblies.ToImmutableArray();

            await ParallelParse(context, cancellationToken);
            var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release, usings:new[]
                {
                    "System",   
                    "System.IO",   
                    "System.Net",   
                    "System.Linq",   
                    "System.Text",   
                    "System.Text.RegularExpressions",   
                    "System.Collections.Generic",
                    "Cryptodd"
                });
            var compilation = CSharpCompilation.Create(context.AssemblyName,
                context.CompilerFileTrees.Select(tree => tree.SyntaxTree ?? throw new NullReferenceException()),
                context.LinkedAssemblies, compilationOptions
                );
            var outputStream = new MemoryStream();
            try
            {
                var pdbStream = new MemoryStream();
                try
                {
                    var result = compilation.Emit(outputStream, pdbStream: pdbStream,
                        cancellationToken: cancellationToken);
                    if (!result.Success)
                    {
                        throw CompilationException.FromDiagnostics(result.Diagnostics, context);
                    }

                    var ret = new CompilerResult(context, outputStream, pdbStream, result,
                        Assembly.Load(outputStream.ToArray(), pdbStream.ToArray()));
                    try
                    {
                        return ret;
                    }
                    catch (Exception)
                    {
                        ret.Dispose();
                        throw;
                    }
                }
                catch (Exception)
                {
                    pdbStream.Dispose();
                    throw;
                }
            }
            catch (Exception)
            {
                outputStream.Dispose();
                throw;
            }
        }

        private static async Task ParallelParse(CompilerContext context, CancellationToken cancellationToken)
        {
            using var semaphore = new SemaphoreSlim(Math.Min(Math.Max(Environment.ProcessorCount, 2), 16));

            async Task<CompilerFileTree> StartParseTask(int i)
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var file = context.CompilerFileTrees[i];
                    using (var reader = File.OpenText(file.Path))
                    {
                        var tree = CSharpSyntaxTree.ParseText(await reader.ReadToEndAsync(),
                            new CSharpParseOptions(LanguageVersion.Latest), cancellationToken: cancellationToken);
                        return new CompilerFileTree(file.Path, tree);
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var tasks = new Task<CompilerFileTree>[context.CompilerFileTrees.Count];
            Parallel.For(0, context.CompilerFileTrees.Count, i => { tasks[i] = StartParseTask(i); });

            for (var i = 0; i < tasks.Length; i++)
            {
                context.CompilerFileTrees[i] = await tasks[i];
            }
        }

        internal static CompilerContext CreateContextFromPath(string path)
        {
            if (!Directory.Exists(path))
            {
                throw new FileNotFoundException($"directory {path} not found");
            }

            var name = Path.GetFileName(path);
            var context = new CompilerContext(name);
            foreach (var file in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            {
                context.CompilerFileTrees.Add(new CompilerFileTree(file, null));
            }

            return context;
        }

        public class CompilationException : Exception
        {
            public ImmutableArray<Diagnostic> Diagnostics { get; internal set; } = ImmutableArray<Diagnostic>.Empty;

            internal CompilationException()
            {
            }

            protected CompilationException(SerializationInfo info, StreamingContext context) : base(info, context)
            {
            }

            internal CompilationException(string message) : base(message)
            {
            }

            internal CompilationException(string message, Exception innerException) : base(message, innerException)
            {
            }

            internal static CompilationException FromDiagnostics(ImmutableArray<Diagnostic> diagnostics,
                CompilerContext context)
            {
                string GetFileFromTree(SyntaxTree? syntaxTree)
                {
                    return context.CompilerFileTrees.Where(tree => tree.SyntaxTree == syntaxTree)
                        .Select(tree => tree.Path).FirstOrDefault() ?? string.Empty;
                }

                string FormatFilePosition(Diagnostic diagnostic)
                {
                    var file = GetFileFromTree(diagnostic.Location.SourceTree);
                    if (string.IsNullOrEmpty(file) && !diagnostic.Location.IsInSource)
                    {
                        return "";
                    }

                    return $"{file}@{diagnostic.Location.GetLineSpan().StartLinePosition} :";
                }

                var message = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(error =>
                        $"{FormatFilePosition(error)} {error.Id} {error.GetMessage()}".Trim())
                    .Aggregate((l, r) => l + "\n" + r);
                return new CompilationException(message) {Diagnostics = diagnostics};
            }

            public IEnumerable<Diagnostic> Errors => Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error);

            public IEnumerable<Diagnostic> Warnings => Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Warning);
        }
    }
}