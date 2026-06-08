using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KnockBox.Plugins.Analyzer;

/// <summary>
/// KB1007 — flags a server projector whose view type is server-only: an
/// <c>AbstractStateProjector&lt;TState, TView&gt;</c> whose <c>TView</c> is a
/// server-only type (e.g. <c>AbstractGameState</c> itself). The projection that
/// crosses the hub must be a plain serializable DTO so raw server state — and its
/// secrets — never reach the wire. (A ref struct such as <c>ReadOnlySpan&lt;byte&gt;</c>
/// cannot be a generic type argument, so the language already prevents it as a
/// <c>TView</c>.)
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class KB1007ProjectionReturnTypeAnalyzer : GatedAnalyzerBase
{
    private const string AbstractStateProjectorFullName =
        "KnockBox.Core.Services.State.Games.Shared.Projection.AbstractStateProjector";

    protected override DiagnosticDescriptor Rule { get; } = new(
        id: "KB1007",
        title: "Projection view type is not a serializable contract",
        messageFormat: "Projector view type '{0}' must be a serializable contract DTO, not a server-only type.",
        category: "KnockBox.Plugins.Sandbox",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "A per-player projection is serialized and sent to the browser, so the view type an " +
            "AbstractStateProjector<TState, TView> produces must be a plain serializable DTO. Returning a " +
            "server-only type (e.g. AbstractGameState) would ship raw state and its secrets to every client.");

    // Runs for server projects (and, defensively, projects that omit the property);
    // never for .Client projects, which have no engine/projector.
    protected override bool ShouldRun(string? pluginKind) => pluginKind != ClientKind;

    protected override void RegisterActions(CompilationStartAnalysisContext context)
        => context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);

    private void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (AnalyzerTypeNames.FullName(baseType.OriginalDefinition) != AbstractStateProjectorFullName
                || baseType.TypeArguments.Length != 2)
            {
                continue;
            }

            var view = baseType.TypeArguments[1];
            if (view.TypeKind == TypeKind.TypeParameter)
                return;

            if (AnalyzerTypeNames.IsServerOnlyType(view))
            {
                var location = type.Locations.Length > 0 ? type.Locations[0] : Location.None;
                context.ReportDiagnostic(Diagnostic.Create(Rule, location, AnalyzerTypeNames.FullName(view)));
            }
            return;
        }
    }
}
