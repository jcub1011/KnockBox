namespace KnockBox.Core.Plugins;

/// <summary>
/// Declarative identity and capability declaration for a game plugin. Produced
/// by parsing the plugin's <c>plugin.json</c> file and is the authoritative
/// source for every piece of plugin metadata — name, route, version, and the
/// set of host capabilities the plugin is allowed to use.
/// </summary>
/// <remarks>
/// The manifest is read by <c>PluginLoader</c> before the plugin's DLL is
/// loaded so identity/capability errors surface without running plugin code.
/// After load, the loader cross-checks the manifest returned by
/// <see cref="IGameModule.Manifest"/> against the on-disk manifest; mismatches
/// reject the plugin.
/// </remarks>
public interface IPluginManifest
{
    /// <summary>Display name shown on the home page (e.g., "Card Counter").</summary>
    string Name { get; }

    /// <summary>Short description shown on the home page.</summary>
    string Description { get; }

    /// <summary>
    /// Route segment used both for navigation (<c>/room/{routeIdentifier}/...</c>)
    /// and as the DI key for the keyed <c>AbstractGameEngine</c> registration.
    /// Must match <c>^[a-z0-9-]+$</c> and be unique across loaded plugins.
    /// </summary>
    string RouteIdentifier { get; }

    /// <summary>
    /// Plugin's own version. Informational only (shown in logs and the admin
    /// dashboard). It does <b>not</b> gate compatibility — that check uses the
    /// <c>KnockBox.Core</c> version the plugin was compiled against, which the
    /// loader reads from the plugin's <c>.deps.json</c>.
    /// </summary>
    Version Version { get; }

    /// <summary>
    /// Simple name of the plugin's primary assembly (no <c>.dll</c> extension),
    /// e.g., <c>"KnockBox.CardCounter"</c>. Required; the loader uses this to
    /// locate the DLL under <c>games/{PluginFolder}/{EntryAssembly}.dll</c>.
    /// </summary>
    string EntryAssembly { get; }

    /// <summary>
    /// Capabilities the plugin has declared. <see cref="IPluginContext"/>
    /// properties corresponding to undeclared capabilities throw
    /// <see cref="PluginCapabilityNotGrantedException"/> on first access.
    /// </summary>
    IReadOnlySet<PluginCapability> Capabilities { get; }

    /// <summary>
    /// Optional plugin-relative path (forward-slash separated, e.g.
    /// <c>"tile.svg"</c> or <c>"assets/tile.svg"</c>) to an SVG the host
    /// renders as the plugin's home-page tile in place of
    /// <see cref="IGameModule.GetButtonContent"/>. The host resolves it against
    /// the plugin's static-asset mount at
    /// <c>_content/{EntryAssembly}/{TileAsset}</c>. <c>null</c> means "render
    /// my Razor tile via GetButtonContent."
    /// </summary>
    string? TileAsset => null;

    /// <summary>
    /// Returns <c>true</c> if <paramref name="capability"/> was declared in this
    /// manifest.
    /// </summary>
    bool HasCapability(PluginCapability capability) => Capabilities.Contains(capability);
}
