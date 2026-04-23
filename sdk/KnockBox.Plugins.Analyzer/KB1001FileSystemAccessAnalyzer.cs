using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KnockBox.Plugins.Analyzer;

/// <summary>
/// KB1001 — flags direct filesystem APIs (<c>File</c>, <c>Directory</c>,
/// <c>FileStream</c>, …). The sanctioned path is <c>IPluginContext.Storage</c>,
/// which stays inside the per-plugin root even if the plugin asks for paths
/// outside it.
/// </summary>
/// <remarks>
/// In-memory I/O shapes (<c>MemoryStream</c>, <c>StreamReader(Stream)</c>,
/// <c>StreamWriter(Stream)</c>, …) and pure-string path helpers on <c>Path</c>
/// are deliberately NOT flagged — they're common building blocks on top of
/// the stream returned by <c>IPluginStorage</c>, and flagging them would
/// produce too much noise. The path-accepting constructors
/// <c>StreamReader(string)</c> and <c>StreamWriter(string)</c> ARE flagged,
/// since they open a file the same way <c>File.Open*</c> does.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class KB1001FileSystemAccessAnalyzer : SandboxAnalyzerBase
{
    protected override DiagnosticDescriptor Rule { get; } = new(
        id: "KB1001",
        title: "Direct filesystem access from a plugin",
        messageFormat: "Plugin code accesses '{0}'. Use IPluginContext.Storage (capability: storage) instead.",
        category: "KnockBox.Plugins.Sandbox",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "Plugin projects must route filesystem access through IPluginContext.Storage so reads and writes " +
            "stay inside the per-plugin sandbox directory. Declare the 'storage' capability in plugin.json to " +
            "receive an IPluginStorage instance. This is a build-time lint and does not prevent reflection-based " +
            "bypass (e.g., Activator.CreateInstance, Type.GetType).");

    protected override ImmutableHashSet<string> BannedTypes { get; } =
        ImmutableHashSet.CreateRange(System.StringComparer.Ordinal, new[]
        {
            "System.IO.File",
            "System.IO.Directory",
            "System.IO.FileInfo",
            "System.IO.DirectoryInfo",
            "System.IO.FileStream",
            "System.IO.FileSystemWatcher",
            "System.IO.DriveInfo",
        });

    protected override ImmutableDictionary<string, ImmutableHashSet<string>> BannedCtorFirstParameters { get; } =
        new Dictionary<string, ImmutableHashSet<string>>(System.StringComparer.Ordinal)
        {
            ["System.IO.StreamReader"] = ImmutableHashSet.Create(System.StringComparer.Ordinal, "System.String"),
            ["System.IO.StreamWriter"] = ImmutableHashSet.Create(System.StringComparer.Ordinal, "System.String"),
        }.ToImmutableDictionary(System.StringComparer.Ordinal);
}
