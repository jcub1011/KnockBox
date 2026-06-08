using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace KnockBox.Plugins.Analyzer;

/// <summary>
/// KB1008 — flags server <c>System.IO</c> filesystem APIs in a WASM <c>.Client</c>
/// assembly. There is no server filesystem in the browser; a client that needs file
/// I/O uses browser File/Blob APIs (JS interop download,
/// <c>Microsoft.AspNetCore.Components.Forms.IBrowserFile</c>) — which are NOT
/// <c>System.IO</c> and are therefore allowed. This is the client-side counterpart
/// to the server-side KB1001.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class KB1008ClientFilesystemAnalyzer : GatedAnalyzerBase
{
    private static readonly ImmutableHashSet<string> BannedTypes =
        ImmutableHashSet.CreateRange(StringComparer.Ordinal, new[]
        {
            "System.IO.File",
            "System.IO.Directory",
            "System.IO.FileInfo",
            "System.IO.DirectoryInfo",
            "System.IO.FileStream",
            "System.IO.FileSystemWatcher",
            "System.IO.DriveInfo",
        });

    protected override DiagnosticDescriptor Rule { get; } = new(
        id: "KB1008",
        title: "Server filesystem access from client UI",
        messageFormat: "Client UI accesses server filesystem API '{0}'. Use browser File/Blob APIs (JS interop / IBrowserFile) instead.",
        category: "KnockBox.Plugins.Sandbox",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "A WASM .Client assembly runs in the browser, which has no server filesystem. Use browser File/Blob " +
            "download (JS interop) or IBrowserFile for uploads. Direct System.IO filesystem types do nothing " +
            "useful in the browser and signal code that was meant for the server.");

    protected override bool ShouldRun(string? pluginKind) => pluginKind == ClientKind;

    protected override void RegisterActions(CompilationStartAnalysisContext context)
        => context.RegisterOperationAction(
            AnalyzeOperation,
            OperationKind.ObjectCreation,
            OperationKind.Invocation,
            OperationKind.PropertyReference,
            OperationKind.FieldReference);

    private void AnalyzeOperation(OperationAnalysisContext context)
    {
        var op = context.Operation;

        ITypeSymbol? containing = op switch
        {
            IObjectCreationOperation o => o.Constructor?.ContainingType,
            IInvocationOperation i => i.TargetMethod.ContainingType,
            IPropertyReferenceOperation p => p.Property.ContainingType,
            IFieldReferenceOperation f => f.Field.ContainingType,
            _ => null,
        };
        if (containing is null)
            return;

        var fullName = AnalyzerTypeNames.FullName(containing);

        if (BannedTypes.Contains(fullName))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, op.Syntax.GetLocation(), fullName));
            return;
        }

        // Path-accepting StreamReader/StreamWriter ctors open a file the same way
        // File.Open* does; the Stream-accepting overloads are fine and not flagged.
        if (op is IObjectCreationOperation creation
            && (fullName == "System.IO.StreamReader" || fullName == "System.IO.StreamWriter"))
        {
            var ctor = creation.Constructor;
            if (ctor is { Parameters.Length: > 0 }
                && AnalyzerTypeNames.FullName(ctor.Parameters[0].Type) == "System.String")
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, op.Syntax.GetLocation(), fullName));
            }
        }
    }
}
