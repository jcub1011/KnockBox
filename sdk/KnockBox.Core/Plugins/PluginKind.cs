namespace KnockBox.Core.Plugins;

/// <summary>
/// Discriminator on <see cref="IPluginManifest.Kind"/> selecting between the two
/// plugin shapes the host knows how to load.
/// </summary>
/// <remarks>
/// Encoded in <c>plugin.json</c> as the case-insensitive string <c>"game"</c> or
/// <c>"library"</c>. The field is optional; manifests that omit it are treated
/// as <see cref="Game"/> so every existing first-party game plugin continues to
/// load unchanged.
/// </remarks>
public enum PluginKind
{
    /// <summary>
    /// A user-facing game plugin. Implements <see cref="IGameModule"/>, registers
    /// exactly one <see cref="Services.Logic.Games.Engines.Shared.AbstractGameEngine"/>
    /// via <see cref="IPluginRegistration.AddGameEngine{TEngine}"/>, and appears
    /// on the home page tile list. The default when <c>kind</c> is absent.
    /// </summary>
    Game,

    /// <summary>
    /// A non-user-facing library plugin. Implements <see cref="ILibraryModule"/>,
    /// registers services other plugins consume (via the shared contracts assembly
    /// listed in <see cref="IPluginManifest.ExportedContracts"/>), and does not
    /// appear on the home page. Loaded before any game plugin so game-side
    /// constructor injection of library-exported services resolves successfully.
    /// </summary>
    Library,
}
