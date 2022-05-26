using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Maxisoft.Plugins.Loader
{
    public sealed class CompilerContext
    {
        public readonly Guid Id = Guid.NewGuid();
        public readonly string AssemblyName;
        public Assembly? RootAssembly { get; internal set; }
        public ImmutableArray<MetadataReference> LinkedAssemblies { get; internal set; } = ImmutableArray<MetadataReference>.Empty;

        public List<CompilerFileTree> CompilerFileTrees { get; internal set; } = new List<CompilerFileTree>(); 

        public CompilerContext(string assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName)) throw new ArgumentException(nameof(assemblyName));
            AssemblyName = assemblyName;
        }
    }

    public readonly struct CompilerFileTree
    {
        public readonly string Path;
        public readonly SyntaxTree? SyntaxTree;

        internal CompilerFileTree(string path, SyntaxTree? syntaxTree)
        {
            Path = path;
            SyntaxTree = syntaxTree;
        }
    }
}