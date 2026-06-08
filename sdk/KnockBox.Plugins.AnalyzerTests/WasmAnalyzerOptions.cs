using System.Collections.Generic;

namespace KnockBox.Plugins.AnalyzerTests;

/// <summary>
/// Global analyzer-config option sets the gated WASM analyzers (KB1005–KB1008)
/// read to decide whether they apply. Mirrors the <c>KnockBoxPluginKind</c> MSBuild
/// property the plugin targets surface via <c>CompilerVisibleProperty</c>.
/// </summary>
internal static class WasmAnalyzerOptions
{
    public static readonly IReadOnlyDictionary<string, string> Client =
        new Dictionary<string, string> { ["build_property.KnockBoxPluginKind"] = "client" };

    public static readonly IReadOnlyDictionary<string, string> Server =
        new Dictionary<string, string> { ["build_property.KnockBoxPluginKind"] = "server" };
}
