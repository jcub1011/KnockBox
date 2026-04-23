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
/// In-memory I/O shapes (<c>MemoryStream</c>, <c>StreamReader</c>,
/// <c>StreamWriter</c>, …) and pure-string path helpers on <c>Path</c> are
/// deliberately NOT flagged — they're common building blocks on top of the
/// stream returned by <c>IPluginStorage</c>, and flagging them would produce
/// too much noise.
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
            "receive an IPluginStorage instance.");

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
}
