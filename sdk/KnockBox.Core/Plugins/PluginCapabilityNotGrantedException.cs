namespace KnockBox.Core.Plugins;

/// <summary>
/// Thrown when plugin code accesses an <see cref="IPluginContext"/> property
/// whose backing <see cref="PluginCapability"/> was not declared in the
/// plugin's <c>plugin.json</c>. The plugin author's fix is always the same:
/// add the missing capability to the manifest, rebuild, redeploy.
/// </summary>
public sealed class PluginCapabilityNotGrantedException : InvalidOperationException
{
    /// <summary>The plugin that attempted the access.</summary>
    public string RouteIdentifier { get; }

    /// <summary>The capability that was missing from the manifest.</summary>
    public PluginCapability Capability { get; }

    public PluginCapabilityNotGrantedException(string routeIdentifier, PluginCapability capability)
        : base(BuildMessage(routeIdentifier, capability))
    {
        RouteIdentifier = routeIdentifier;
        Capability = capability;
    }

    private static string BuildMessage(string routeIdentifier, PluginCapability capability) =>
        $"Plugin [{routeIdentifier}] accessed the [{capability}] capability but did not declare it in plugin.json. " +
        $"Add \"{capability.ToString().ToLowerInvariant()}\" to the manifest's 'capabilities' array to grant it.";
}
