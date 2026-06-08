using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KnockBox.Plugins.Analyzer;

/// <summary>
/// Base for the WASM tri-split boundary analyzers (KB1005–KB1008), which — unlike
/// the always-on sandbox rules KB1001–KB1004 — only apply in a specific project
/// context. The project surfaces its role via the MSBuild property
/// <c>KnockBoxPluginKind</c> (<c>client</c> or <c>server</c>), made compiler-visible
/// by the plugin targets files; this base reads it in a compilation-start action and
/// registers the rule's analysis only when <see cref="ShouldRun"/> agrees. Client
/// rules run in <c>.Client</c> projects; the server projection rule runs everywhere
/// else (server plugins and, defensively, projects that omit the property).
/// </summary>
public abstract class GatedAnalyzerBase : DiagnosticAnalyzer
{
    internal const string PluginKindProperty = "build_property.KnockBoxPluginKind";
    internal const string ClientKind = "client";
    internal const string ServerKind = "server";

    protected abstract DiagnosticDescriptor Rule { get; }

    public sealed override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <summary>
    /// Whether this analyzer runs for a project whose <c>KnockBoxPluginKind</c> is
    /// <paramref name="pluginKind"/> (<see langword="null"/> when the property is unset).
    /// </summary>
    protected abstract bool ShouldRun(string? pluginKind);

    /// <summary>Registers the rule's analysis actions. Only invoked when <see cref="ShouldRun"/> is true.</summary>
    protected abstract void RegisterActions(CompilationStartAnalysisContext context);

    public sealed override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            start.Options.AnalyzerConfigOptionsProvider.GlobalOptions
                .TryGetValue(PluginKindProperty, out var kind);
            var normalized = string.IsNullOrWhiteSpace(kind) ? null : kind!.Trim().ToLowerInvariant();
            if (ShouldRun(normalized))
                RegisterActions(start);
        });
    }
}
