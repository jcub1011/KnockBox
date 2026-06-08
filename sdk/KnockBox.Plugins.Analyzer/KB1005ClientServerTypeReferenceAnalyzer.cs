using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace KnockBox.Plugins.Analyzer;

/// <summary>
/// KB1005 — flags a WASM <c>.Client</c> assembly referencing a server-only KnockBox
/// type (anything under <c>KnockBox.Core.*</c> except <c>KnockBox.Core.Client.*</c>,
/// anything under <c>KnockBox.Platform.*</c>, or the server WordService oracle). A
/// client UI may reference only <c>KnockBox.Core.Client</c> and a game's
/// <c>*.Contracts</c> DTOs; pulling in <c>AbstractGameState</c> / the engine drags
/// server-only surface (filesystem, locks, event managers) into the browser.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class KB1005ClientServerTypeReferenceAnalyzer : GatedAnalyzerBase
{
    protected override DiagnosticDescriptor Rule { get; } = new(
        id: "KB1005",
        title: "Client UI references a server-only type",
        messageFormat: "Client UI references server-only type '{0}'. A .Client assembly may reference only KnockBox.Core.Client and *.Contracts.",
        category: "KnockBox.Plugins.Sandbox",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "A WASM .Client assembly is downloaded into the browser, so it must bind only the WASM-safe " +
            "KnockBox.Core.Client SDK and a game's *.Contracts DTOs. Referencing server-only KnockBox types " +
            "(AbstractGameState, the engine, KnockBox.Platform, the WordService oracle) drags server-only " +
            "surface into the browser and breaks the client/server split.");

    protected override bool ShouldRun(string? pluginKind) => pluginKind == ClientKind;

    protected override void RegisterActions(CompilationStartAnalysisContext context)
    {
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
        context.RegisterOperationAction(
            AnalyzeOperation,
            OperationKind.ObjectCreation,
            OperationKind.Invocation,
            OperationKind.PropertyReference,
            OperationKind.FieldReference);
    }

    private void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (type.BaseType is { } baseType && AnalyzerTypeNames.IsServerOnlyType(baseType))
            ReportOnType(context, type, baseType);

        foreach (var iface in type.Interfaces)
        {
            if (AnalyzerTypeNames.IsServerOnlyType(iface))
                ReportOnType(context, type, iface);
        }
    }

    private void ReportOnType(SymbolAnalysisContext context, INamedTypeSymbol declaring, ITypeSymbol referenced)
    {
        var location = declaring.Locations.Length > 0 ? declaring.Locations[0] : Location.None;
        context.ReportDiagnostic(Diagnostic.Create(Rule, location, AnalyzerTypeNames.FullName(referenced)));
    }

    private void AnalyzeOperation(OperationAnalysisContext context)
    {
        ITypeSymbol? referenced = context.Operation switch
        {
            IObjectCreationOperation o => o.Constructor?.ContainingType,
            IInvocationOperation i => i.TargetMethod.ContainingType,
            IPropertyReferenceOperation p => p.Property.ContainingType,
            IFieldReferenceOperation f => f.Field.ContainingType,
            _ => null,
        };

        if (referenced is not null && AnalyzerTypeNames.IsServerOnlyType(referenced))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rule, context.Operation.Syntax.GetLocation(), AnalyzerTypeNames.FullName(referenced)));
        }
    }
}
