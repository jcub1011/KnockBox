namespace KnockBox.Core.Plugins;

/// <summary>
/// A capability a plugin must declare in its <c>plugin.json</c> to access the
/// corresponding <see cref="IPluginContext"/> surface at runtime. Accessing a
/// context property whose capability was not declared throws
/// <see cref="PluginCapabilityNotGrantedException"/>.
/// </summary>
public enum PluginCapability
{
    /// <summary>
    /// Grants access to <see cref="IPluginContext.Configuration"/> — a
    /// per-plugin <c>IConfiguration</c> section rooted at
    /// <c>Plugins:{RouteIdentifier}</c>.
    /// </summary>
    Config,

    /// <summary>
    /// Grants access to <see cref="IPluginContext.Storage"/> — a per-plugin
    /// <see cref="IPluginStorage"/> rooted under the host's content root.
    /// </summary>
    Storage,
}
