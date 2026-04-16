using System.Reflection;
using KnockBox.Core.Plugins;

namespace KnockBox.Platform;

/// <summary>
/// Configuration options for the KnockBox Platform shared host runtime.
/// </summary>
public sealed class KnockBoxPlatformOptions
{
    /// <summary>Application title shown in the header when no game session is active.</summary>
    public string AppTitle { get; set; } = "Knockbox";

    /// <summary>Large hero title rendered on the home page.</summary>
    public string HomeHeroTitle { get; set; } = "Knockbox";

    /// <summary>Browser tab / page title on the home page.</summary>
    public string HomePageTitle { get; set; } = "Knockbox Games";

    /// <summary>How game plugins are discovered. Defaults to directory scanning.</summary>
    public PluginDiscoveryMode PluginDiscovery { get; set; } = PluginDiscoveryMode.Directory;

    /// <summary>
    /// Relative (to <c>AppContext.BaseDirectory</c>) or absolute path to the
    /// directory that contains game plugin folders. Only used in
    /// <see cref="PluginDiscoveryMode.Directory"/> mode.
    /// </summary>
    public string PluginsPath { get; set; } = "games";

    /// <summary>Modules registered via <see cref="KnockBoxPlatformOptionsExtensions.AddGameModule{T}"/>.</summary>
    internal List<IGameModule> ExplicitModules { get; } = [];

    /// <summary>Assemblies registered via explicit module registration.</summary>
    internal List<Assembly> ExplicitAssemblies { get; } = [];
}
