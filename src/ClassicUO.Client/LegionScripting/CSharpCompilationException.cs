using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ClassicUO.LegionScripting;

/// <summary>
/// Exception thrown when C# script compilation fails
/// </summary>
public class CSharpCompilationException : Exception
{
    public IEnumerable<Diagnostic> Diagnostics { get; }

    public CSharpCompilationException(IEnumerable<Diagnostic> diagnostics)
        : base(BuildMessage(diagnostics))
    {
        Diagnostics = diagnostics;
    }

    private static string BuildMessage(IEnumerable<Diagnostic> diagnostics)
    {
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Count == 0)
            return "Compilation failed with unknown errors.";

        return $"Compilation failed with {errors.Count} error(s):\n" +
               string.Join("\n", errors.Select(d => d.GetMessage()));
    }
}
