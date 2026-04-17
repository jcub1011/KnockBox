using KnockBox.Core.Plugins;

namespace KnockBox.Platform;

/// <summary>
/// Fluent extension methods for <see cref="KnockBoxPlatformOptions"/>.
/// </summary>
public static class KnockBoxPlatformOptionsExtensions
{
    /// <summary>
    /// Appends a game module to <see cref="KnockBoxPlatformOptions.ExplicitModules"/>.
    /// </summary>
    /// <remarks>
    /// Callers must also set <see cref="KnockBoxPlatformOptions.PluginDiscovery"/>
    /// to <see cref="PluginDiscoveryMode.Explicit"/>; this method does not flip
    /// the mode implicitly. <c>AddKnockBoxPlatform</c> throws if explicit modules
    /// are registered while <c>PluginDiscovery</c> is set to
    /// <see cref="PluginDiscoveryMode.Directory"/>, so the footgun is loud
    /// either way.
    /// </remarks>
    public static KnockBoxPlatformOptions AddGameModule<TModule>(
        this KnockBoxPlatformOptions options)
        where TModule : IGameModule, new()
    {
        var module = new TModule();
        options.ExplicitModules.Add(module);

        var assembly = typeof(TModule).Assembly;
        if (!options.ExplicitAssemblies.Contains(assembly))
            options.ExplicitAssemblies.Add(assembly);

        return options;
    }
}
