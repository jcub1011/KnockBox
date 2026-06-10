using System.Reflection;
using KnockBox.Core.Plugins;

namespace KnockBox.Platform;

/// <summary>
/// Configuration options for the KnockBox Platform shared host runtime.
/// </summary>
/// <remarks>
/// This object is populated inside the <c>configure</c> callback passed to
/// <c>AddKnockBoxPlatform</c>, which runs once during <c>WebApplicationBuilder</c>
/// setup on the startup thread. Mutating it from multiple threads is not
/// supported; the contained collections (<see cref="PluginsPaths"/>,
/// <see cref="ExplicitModules"/>, <see cref="ExplicitAssemblies"/>) are
/// plain <c>List&lt;T&gt;</c> and therefore not thread-safe.
/// </remarks>
public sealed class KnockBoxPlatformOptions
{
    /// <summary>Branding strings used by the built-in home page and header.</summary>
    public BrandingOptions Branding { get; } = new();

    /// <summary>
    /// Maximum size, in bytes, of a single file accepted by the generic plugin
    /// upload endpoint (<c>POST /api/games/upload</c>). The dispatcher rejects a
    /// larger body with <c>413 Payload Too Large</c>. Defaults to 2 MB, which
    /// covers the first-party CSV word-pool upload with headroom; a host can raise
    /// it for plugins that need bigger uploads. (Kestrel's own request-body limit
    /// remains the outer hard cap.)
    /// </summary>
    public long MaxUploadBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>How game plugins are discovered. Defaults to directory scanning.</summary>
    public PluginDiscoveryMode PluginDiscovery { get; set; } = PluginDiscoveryMode.Directory;

    /// <summary>
    /// Relative (to <c>AppContext.BaseDirectory</c>) or absolute paths to the
    /// directories that contain game plugin folders. Only used in
    /// <see cref="PluginDiscoveryMode.Directory"/> mode.
    /// Defaults to a single entry: <c>"games"</c>. Production hosts that need
    /// to scan multiple folders (e.g. first-party + third-party) should clear
    /// the list and add the absolute paths they want, based on whatever
    /// host-local settings they own.
    /// </summary>
    public IList<string> PluginsPaths { get; } = ["games"];

    /// <summary>
    /// Relative (to <c>AppContext.BaseDirectory</c>) or absolute paths to the
    /// directories that contain library plugin folders. Only used in
    /// <see cref="PluginDiscoveryMode.Directory"/> mode.
    /// Defaults to a single entry: <c>"libraries"</c>. Library plugins are loaded
    /// and registered before any game plugin so games consuming library-exported
    /// services resolve correctly. The folder is only a convention — a plugin
    /// folder's <see cref="IPluginManifest.Kind"/> still wins if a misplaced
    /// game manifest is found under <c>libraries/</c> or vice-versa.
    /// </summary>
    public IList<string> LibrariesPaths { get; } = ["libraries"];

    private readonly List<IGameModule> _explicitModules = [];
    private readonly List<Assembly> _explicitAssemblies = [];

    /// <summary>Modules registered via <see cref="KnockBoxPlatformOptionsExtensions.AddGameModule{T}"/>.</summary>
    internal IReadOnlyList<IGameModule> ExplicitModules => _explicitModules;

    /// <summary>Assemblies registered via explicit module registration.</summary>
    internal IReadOnlyList<Assembly> ExplicitAssemblies => _explicitAssemblies;

    internal void AddExplicitModule(IGameModule module) => _explicitModules.Add(module);

    internal void AddExplicitAssembly(Assembly assembly)
    {
        if (!_explicitAssemblies.Contains(assembly))
            _explicitAssemblies.Add(assembly);
    }
}

/// <summary>
/// Strings used by the platform's built-in home page and header. Expose your
/// own branding through <see cref="KnockBoxPlatformOptions.Branding"/>.
/// </summary>
public sealed class BrandingOptions
{
    /// <summary>Application title shown in the header when no game session is active.</summary>
    public string AppTitle { get; set; } = "Knockbox";

    /// <summary>Large hero title rendered on the home page.</summary>
    public string HomeHeroTitle { get; set; } = "Knockbox";

    /// <summary>Browser tab / page title on the home page.</summary>
    public string HomePageTitle { get; set; } = "Knockbox Games";
}
