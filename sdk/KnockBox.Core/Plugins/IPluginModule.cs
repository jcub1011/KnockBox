namespace KnockBox.Core.Plugins;

/// <summary>
/// Base contract every plugin module implements. The host's
/// <c>PluginLoader</c> reflects for this interface inside each plugin assembly
/// and the registration pipeline calls <see cref="RegisterServices"/> exactly
/// once per loaded plugin.
/// </summary>
/// <remarks>
/// Plugins do not implement this interface directly — they implement one of the
/// two specializations:
/// <list type="bullet">
///   <item><see cref="IGameModule"/> for a user-facing game plugin.</item>
///   <item><see cref="ILibraryModule"/> for a non-user-facing library plugin
///   that exports services other plugins consume.</item>
/// </list>
/// The host enforces "kind matches type" at load time: a manifest with
/// <see cref="IPluginManifest.Kind"/> = <see cref="PluginKind.Game"/> must
/// resolve to an <see cref="IGameModule"/> implementation, and likewise for
/// libraries. Mismatches reject the plugin with a clear log entry.
/// </remarks>
public interface IPluginModule
{
    /// <summary>
    /// The plugin's manifest. Must agree with the <c>plugin.json</c> the loader
    /// parsed from disk; mismatched manifests cause the loader to reject the
    /// plugin. A plugin almost always populates this by reading its own
    /// embedded <c>plugin.json</c> via
    /// <see cref="PluginManifest.FromEmbeddedResource(System.Reflection.Assembly)"/>.
    /// </summary>
    IPluginManifest Manifest { get; }

    /// <summary>
    /// Registers plugin-owned services. For games this includes the engine
    /// (via <see cref="IPluginRegistration.AddGameEngine{TEngine}"/>) and any
    /// plugin-private helpers. For libraries this is where the
    /// <see cref="IPluginManifest.ExportedContracts"/>-implementing service
    /// types are registered so consuming plugins can resolve them. The host
    /// never hands plugins a raw <c>IServiceCollection</c>.
    /// </summary>
    void RegisterServices(IPluginRegistration registration);
}
