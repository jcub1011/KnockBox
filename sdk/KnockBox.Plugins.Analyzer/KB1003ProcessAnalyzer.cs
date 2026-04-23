using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KnockBox.Plugins.Analyzer;

/// <summary>
/// KB1003 — flags process launch and inspection. Plugins are gameplay code and
/// have no legitimate reason to spawn or introspect OS processes.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class KB1003ProcessAnalyzer : SandboxAnalyzerBase
{
    protected override DiagnosticDescriptor Rule { get; } = new(
        id: "KB1003",
        title: "Process launch from a plugin",
        messageFormat: "Plugin code launches or inspects processes via '{0}'. Process launch is not permitted from plugins.",
        category: "KnockBox.Plugins.Sandbox",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "Plugin projects cannot launch, enumerate, or control OS processes. Gameplay code should have no " +
            "reason to reach for Process — raise an issue if you believe you need one.");

    protected override ImmutableHashSet<string> BannedTypes { get; } =
        ImmutableHashSet.CreateRange(System.StringComparer.Ordinal, new[]
        {
            "System.Diagnostics.Process",
            "System.Diagnostics.ProcessStartInfo",
        });
}
