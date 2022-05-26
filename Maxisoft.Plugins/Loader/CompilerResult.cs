using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;

namespace Maxisoft.Plugins.Loader
{
    public class CompilerResult : IDisposable
    {
        public ImmutableArray<Diagnostic> Diagnostics => EmitResult.Diagnostics;
        public readonly Guid Id;
        public readonly CompilerContext CompilerContext;

        public readonly Stream? ExecutableStream;
        public readonly Stream? PdbStream;
        public readonly Assembly? Assembly;
        internal readonly EmitResult EmitResult;

        public IEnumerable<Diagnostic> Errors => Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error);

        public IEnumerable<Diagnostic> Warnings => Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Warning);

        internal CompilerResult(CompilerContext context, Stream? executableStream, Stream? pdbStream, EmitResult emitResult, Assembly? assembly)
        {
            CompilerContext = context;
            Id = context.Id;
            ExecutableStream = executableStream;
            PdbStream = pdbStream;
            EmitResult = emitResult;
            Assembly = assembly;
        }

        public void Dispose()
        {
            ExecutableStream?.Dispose();
            PdbStream?.Dispose();
        }
    }
}