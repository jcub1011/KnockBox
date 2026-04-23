using KnockBox.Core.Plugins;
using Microsoft.Extensions.Configuration;

namespace KnockBox.Platform.Plugins;

/// <summary>
/// Concrete <see cref="IPluginContext"/> built by the loader for one plugin.
/// Holds the manifest, scoped logger/config/storage, and throws
/// <see cref="PluginCapabilityNotGrantedException"/> from the
/// <see cref="Configuration"/> and <see cref="Storage"/> property getters when
/// the corresponding capability wasn't declared in <c>plugin.json</c>.
/// </summary>
internal sealed class DefaultPluginContext(
    IPluginManifest manifest,
    ILogger logger,
    IConfiguration configuration,
    IPluginStorage storage) : IPluginContext
{
    public IPluginManifest Manifest { get; } = manifest;

    public ILogger Logger { get; } = logger;

    public IConfiguration Configuration =>
        Manifest.HasCapability(PluginCapability.Config)
            ? configuration
            : throw new PluginCapabilityNotGrantedException(Manifest.RouteIdentifier, PluginCapability.Config);

    public IPluginStorage Storage =>
        Manifest.HasCapability(PluginCapability.Storage)
            ? storage
            : throw new PluginCapabilityNotGrantedException(Manifest.RouteIdentifier, PluginCapability.Storage);
}
