using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KnockBox.Plugins.Analyzer;

/// <summary>
/// KB1004 — flags raw environment / configuration reads. The sanctioned path
/// is <c>IPluginContext.Configuration</c>, which is scoped to the plugin's
/// own <c>Plugins:{RouteIdentifier}</c> section.
/// </summary>
/// <remarks>
/// The full <c>System.Environment</c> class isn't banned outright —
/// <c>Environment.NewLine</c>, <c>Environment.ProcessorCount</c>, etc. are
/// benign. Only the environment-variable accessors and
/// <c>ExpandEnvironmentVariables</c> are member-level banned.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class KB1004EnvironmentAnalyzer : SandboxAnalyzerBase
{
    protected override DiagnosticDescriptor Rule { get; } = new(
        id: "KB1004",
        title: "Raw environment or configuration access from a plugin",
        messageFormat: "Plugin code reads '{0}'. Use IPluginContext.Configuration (capability: config) instead.",
        category: "KnockBox.Plugins.Sandbox",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "Plugin projects must read configuration through IPluginContext.Configuration so the values come " +
            "from the plugin's own Plugins:{RouteIdentifier} section. Declare the 'config' capability in " +
            "plugin.json to receive an IConfiguration instance.");

    protected override ImmutableHashSet<string> BannedTypes { get; } = ImmutableHashSet<string>.Empty;

    protected override ImmutableHashSet<string> BannedMembers { get; } =
        ImmutableHashSet.CreateRange(System.StringComparer.Ordinal, new[]
        {
            "System.Environment.GetEnvironmentVariable",
            "System.Environment.GetEnvironmentVariables",
            "System.Environment.SetEnvironmentVariable",
            "System.Environment.ExpandEnvironmentVariables",
        });
}
