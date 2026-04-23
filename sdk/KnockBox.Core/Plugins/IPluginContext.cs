using Microsoft.Extensions.Configuration;

namespace KnockBox.Core.Plugins;

/// <summary>
/// Runtime-facing bundle of host services scoped to a single plugin. Injected
/// into plugin services via standard DI; a plugin only ever sees its own
/// context because the DI wiring keys each context by plugin route and the
/// plugin's services resolve it through a closure captured at registration
/// time.
/// </summary>
public interface IPluginContext
{
    /// <summary>The manifest for this plugin. Same instance the loader validated.</summary>
    IPluginManifest Manifest { get; }

    /// <summary>
    /// Logger with category <c>Plugins.{RouteIdentifier}</c> so operators can
    /// filter by plugin in the host's configured Serilog sinks.
    /// </summary>
    ILogger Logger { get; }

    /// <summary>
    /// Configuration rooted at <c>Plugins:{RouteIdentifier}</c>. Accessing this
    /// property when the <see cref="PluginCapability.Config"/> capability was
    /// not declared in the manifest throws
    /// <see cref="PluginCapabilityNotGrantedException"/>.
    /// </summary>
    IConfiguration Configuration { get; }

    /// <summary>
    /// Filesystem rooted under the host's content root at a per-plugin
    /// directory. Accessing this property when the
    /// <see cref="PluginCapability.Storage"/> capability was not declared in
    /// the manifest throws <see cref="PluginCapabilityNotGrantedException"/>.
    /// </summary>
    IPluginStorage Storage { get; }
}
