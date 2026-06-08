using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KnockBox.Plugins.Analyzer;

/// <summary>
/// KB1006 — flags a WASM <c>.Client</c> page whose hub view type does not come from
/// a <c>*.Contracts</c> assembly. The projected view a client binds (the
/// <c>TView</c> of <c>HubLobbyPageBase&lt;TView&gt;</c>) must be a shared contract
/// DTO, so all server interaction crosses the typed contract boundary and the client
/// can't reach into a raw server projection.
/// </summary>
/// <remarks>
/// Known limitation: the free-string <c>SubmitCommandAsync("...")</c> overload has
/// no typed hook, so command names are not statically verified yet. This rule covers
/// the view-type half of the boundary (the security-relevant one); typed-command
/// verification follows once a real tri-split game establishes the convention.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class KB1006ClientContractBoundaryAnalyzer : GatedAnalyzerBase
{
    private const string HubLobbyPageBaseFullName = "KnockBox.Core.Client.Components.HubLobbyPageBase";

    protected override DiagnosticDescriptor Rule { get; } = new(
        id: "KB1006",
        title: "Client hub view type is not a contract DTO",
        messageFormat: "Client hub view type '{0}' must come from a *.Contracts assembly so server interaction stays on the typed contract boundary.",
        category: "KnockBox.Plugins.Sandbox",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "A HubLobbyPageBase<TView> in a .Client project must project into a *.Contracts DTO. Binding a " +
            "client-local or server type as the view bypasses the shared contract, which is the boundary that " +
            "keeps the client from reaching into a raw server projection (and its secrets).");

    protected override bool ShouldRun(string? pluginKind) => pluginKind == ClientKind;

    protected override void RegisterActions(CompilationStartAnalysisContext context)
        => context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);

    private void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (AnalyzerTypeNames.FullName(baseType.OriginalDefinition) != HubLobbyPageBaseFullName
                || baseType.TypeArguments.Length != 1)
            {
                continue;
            }

            var view = baseType.TypeArguments[0];
            // An open generic intermediate (e.g. an abstract MyBase<T> : HubLobbyPageBase<T>)
            // can't be judged here — the concrete subclass is checked instead.
            if (view.TypeKind == TypeKind.TypeParameter)
                return;

            if (!AnalyzerTypeNames.IsContractsType(view))
            {
                var location = type.Locations.Length > 0 ? type.Locations[0] : Location.None;
                context.ReportDiagnostic(Diagnostic.Create(Rule, location, AnalyzerTypeNames.FullName(view)));
            }
            return;
        }
    }
}
