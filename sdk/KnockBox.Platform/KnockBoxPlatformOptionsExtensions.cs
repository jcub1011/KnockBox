using KnockBox.Core.Plugins;

namespace KnockBox.Platform;

/// <summary>
/// Fluent extension methods for <see cref="KnockBoxPlatformOptions"/>.
/// </summary>
public static class KnockBoxPlatformOptionsExtensions
{
    /// <summary>
    /// Registers a game module explicitly (sets discovery mode to
    /// <see cref="PluginDiscoveryMode.Explicit"/> automatically).
    /// </summary>
    public static KnockBoxPlatformOptions AddGameModule<TModule>(
        this KnockBoxPlatformOptions options)
        where TModule : IGameModule, new()
    {
        options.PluginDiscovery = PluginDiscoveryMode.Explicit;

        var module = new TModule();
        options.ExplicitModules.Add(module);

        var assembly = typeof(TModule).Assembly;
        if (!options.ExplicitAssemblies.Contains(assembly))
            options.ExplicitAssemblies.Add(assembly);

        return options;
    }
}
