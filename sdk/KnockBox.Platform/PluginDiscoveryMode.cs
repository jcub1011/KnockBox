namespace KnockBox.Platform;

/// <summary>
/// Controls how the platform discovers game plugin modules.
/// </summary>
public enum PluginDiscoveryMode
{
    /// <summary>
    /// Scans a directory (default: <c>games/</c>) for plugin assemblies at startup.
    /// Each plugin is loaded into its own <c>AssemblyLoadContext</c>.
    /// </summary>
    Directory,

    /// <summary>
    /// Modules are registered explicitly via <c>AddGameModule&lt;T&gt;()</c>.
    /// No directory scanning or ALC isolation. Ideal for dev hosts with
    /// direct project references.
    /// </summary>
    Explicit,
}
