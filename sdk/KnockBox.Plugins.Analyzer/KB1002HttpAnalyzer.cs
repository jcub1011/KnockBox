using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KnockBox.Plugins.Analyzer;

/// <summary>
/// KB1002 — flags direct HTTP and raw-socket APIs. Plugins do not yet have a
/// capability-gated HTTP surface; outbound network traffic from plugin code
/// is not supported today.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class KB1002HttpAnalyzer : SandboxAnalyzerBase
{
    protected override DiagnosticDescriptor Rule { get; } = new(
        id: "KB1002",
        title: "Direct HTTP or raw network access from a plugin",
        messageFormat: "Plugin code references '{0}'. Capability-gated HTTP is not yet available; outbound network traffic from plugins is not supported.",
        category: "KnockBox.Plugins.Sandbox",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "Plugin projects cannot open outbound HTTP or raw-socket connections. When a future KnockBox " +
            "release ships IPluginHttpClient, this rule will direct callers there.");

    protected override ImmutableHashSet<string> BannedTypes { get; } =
        ImmutableHashSet.CreateRange(System.StringComparer.Ordinal, new[]
        {
            "System.Net.Http.HttpClient",
            "System.Net.Http.HttpClientHandler",
            "System.Net.Http.HttpMessageHandler",
            "System.Net.Http.HttpRequestMessage",
            "System.Net.Http.SocketsHttpHandler",
            "System.Net.WebClient",
            "System.Net.HttpWebRequest",
            "System.Net.WebRequest",
            "System.Net.Sockets.Socket",
            "System.Net.Sockets.TcpClient",
            "System.Net.Sockets.TcpListener",
            "System.Net.Sockets.UdpClient",
        });
}
