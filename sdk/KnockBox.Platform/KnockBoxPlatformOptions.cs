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
    public IList<string> PluginsPaths { get; } = new List<string> { "games" };

    /// <summary>Modules registered via <see cref="KnockBoxPlatformOptionsExtensions.AddGameModule{T}"/>.</summary>
    internal List<IGameModule> ExplicitModules { get; } = [];

    /// <summary>Assemblies registered via explicit module registration.</summary>
    internal List<Assembly> ExplicitAssemblies { get; } = [];
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
