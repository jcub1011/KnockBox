using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KnockBox.Plugins.Analyzer;

/// <summary>
/// KB1003 — flags process launch and host shutdown. Plugins are gameplay code
/// and have no legitimate reason to spawn or introspect OS processes, nor to
/// terminate the host process that's running them.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class KB1003ProcessAnalyzer : SandboxAnalyzerBase
{
    protected override DiagnosticDescriptor Rule { get; } = new(
        id: "KB1003",
        title: "Process launch or host shutdown from a plugin",
        messageFormat: "Plugin code launches processes or terminates the host via '{0}'. This is not permitted from plugins.",
        category: "KnockBox.Plugins.Sandbox",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "Plugin projects cannot launch, enumerate, or control OS processes, and cannot terminate the host " +
            "process (Environment.Exit / Environment.FailFast). Gameplay code should have no reason to reach " +
            "for these — raise an issue if you believe you need one. This is a build-time lint and does not " +
            "prevent reflection-based bypass (e.g., Activator.CreateInstance, Type.GetType).");

    protected override ImmutableHashSet<string> BannedTypes { get; } =
        ImmutableHashSet.CreateRange(System.StringComparer.Ordinal, new[]
        {
            "System.Diagnostics.Process",
            "System.Diagnostics.ProcessStartInfo",
        });

    protected override ImmutableHashSet<string> BannedMembers { get; } =
        ImmutableHashSet.CreateRange(System.StringComparer.Ordinal, new[]
        {
            "System.Environment.Exit",
            "System.Environment.FailFast",
        });
}
