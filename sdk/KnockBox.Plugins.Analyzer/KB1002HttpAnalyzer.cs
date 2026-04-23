using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace KnockBox.Plugins.Analyzer;

/// <summary>
/// KB1002 — flags direct outbound-network and name-resolution APIs: HTTP,
/// raw sockets, DNS lookups, ICMP ping, network-interface enumeration, and
/// SMTP. Outbound network traffic from plugin code is not supported.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class KB1002HttpAnalyzer : SandboxAnalyzerBase
{
    protected override DiagnosticDescriptor Rule { get; } = new(
        id: "KB1002",
        title: "Direct outbound-network or name-resolution access from a plugin",
        messageFormat: "Plugin code references '{0}'. Outbound network traffic from plugins is not supported.",
        category: "KnockBox.Plugins.Sandbox",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "Plugin projects cannot open outbound HTTP / raw-socket connections, perform DNS lookups, send " +
            "ICMP traffic, enumerate local network interfaces, or send SMTP mail. This is a build-time lint " +
            "and does not prevent reflection-based bypass (e.g., Activator.CreateInstance, Type.GetType).");

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
            "System.Net.Dns",
            "System.Net.NetworkInformation.Ping",
            "System.Net.NetworkInformation.NetworkInterface",
            "System.Net.Mail.SmtpClient",
        });
}
