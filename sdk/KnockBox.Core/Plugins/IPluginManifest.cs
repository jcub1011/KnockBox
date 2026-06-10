namespace KnockBox.Core.Plugins;

/// <summary>
/// Declarative identity and capability declaration for a plugin (game or
/// library). Produced by parsing the plugin's <c>plugin.json</c> file and is
/// the authoritative source for every piece of plugin metadata — name, route,
/// version, kind, exported contracts, and the set of host capabilities the
/// plugin is allowed to use.
/// </summary>
/// <remarks>
/// The manifest is read by <c>PluginLoader</c> before the plugin's DLL is
/// loaded so identity/capability errors surface without running plugin code.
/// After load, the loader cross-checks the manifest returned by
/// <see cref="IPluginModule.Manifest"/> against the on-disk manifest;
/// mismatches reject the plugin.
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
    /// For library plugins (<see cref="Kind"/> = <see cref="PluginKind.Library"/>)
    /// this is not used for navigation but still acts as the per-plugin DI key
    /// for the keyed <see cref="IPluginContext"/> registration.
    /// </summary>
    string RouteIdentifier { get; }

    /// <summary>
    /// Plugin's own version. For game plugins this is informational only (shown
    /// in logs and the admin dashboard). For library plugins this MUST be a
    /// strict <c>Major.Minor.Patch</c> SemVer: the loader uses Major.Minor to
    /// group library plugins for side-by-side coexistence and Patch to pick a
    /// winner within a group.
    /// </summary>
    Version Version { get; }

    /// <summary>
    /// Simple name of the plugin's primary assembly (no <c>.dll</c> extension),
    /// e.g., <c>"KnockBox.CardCounter"</c>. Required; the loader uses this to
    /// locate the DLL under <c>games/{PluginFolder}/{EntryAssembly}.dll</c> or
    /// <c>libraries/{PluginFolder}/{EntryAssembly}.dll</c>.
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
    /// renders as the plugin's home-page tile. The host resolves it against
    /// the plugin's static-asset mount at
    /// <c>_content/{EntryAssembly}/{TileAsset}</c>. <c>null</c> means the host
    /// renders a hot-pink fallback labeled with the plugin's
    /// <see cref="Name"/>. Library plugins have no tile and ignore this field.
    /// </summary>
    string? TileAsset => null;

    /// <summary>
    /// When <c>true</c>, the host overlays a shared "Work In Progress" SVG on
    /// top of the plugin's tile and desaturates the underlying art. Intended
    /// to flag a game that's still being built so it's visible on the home
    /// page without claiming to be ready. Defaults to <c>false</c>.
    /// </summary>
    bool WorkInProgress => false;

    /// <summary>
    /// What kind of plugin this is. Defaults to <see cref="PluginKind.Game"/>
    /// for backwards compatibility with manifests written before library
    /// plugins existed. Encoded in <c>plugin.json</c> as the case-insensitive
    /// string <c>"game"</c> or <c>"library"</c>.
    /// </summary>
    PluginKind Kind => PluginKind.Game;

    /// <summary>
    /// For library plugins only: the simple names (no <c>.dll</c> extension) of
    /// the contracts assemblies this library exports. The loader promotes each
    /// listed DLL into the default
    /// <see cref="System.Runtime.Loader.AssemblyLoadContext"/> at startup so
    /// consuming plugins see identical CLR types for the contract interfaces.
    /// Always empty for game plugins. Game manifests that declare non-empty
    /// <see cref="ExportedContracts"/> are rejected at parse time.
    /// </summary>
    IReadOnlyList<string> ExportedContracts => Array.Empty<string>();

    /// <summary>
    /// Optional CSS color (hex, e.g. <c>"#06080f"</c>) the host uses as the
    /// background of this game's entries in the home-page play log, so the log
    /// echoes the game's own palette. <c>null</c> falls back to the default card
    /// background.
    /// </summary>
    string? BackgroundColor => null;

    /// <summary>
    /// Optional CSS color (hex, e.g. <c>"#eaf2ff"</c>) the host uses as the text
    /// color of this game's entries in the home-page play log. <c>null</c> falls
    /// back to the default text colors.
    /// </summary>
    string? FontColor => null;

    /// <summary>
    /// Returns <c>true</c> if <paramref name="capability"/> was declared in this
    /// manifest.
    /// </summary>
    bool HasCapability(PluginCapability capability) => Capabilities.Contains(capability);
}
