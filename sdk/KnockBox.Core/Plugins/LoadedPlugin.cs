using System.Reflection;
using System.Runtime.Loader;

namespace KnockBox.Core.Plugins;

/// <summary>
/// One successfully-loaded plugin: the <see cref="IPluginModule"/> instance,
/// its (disk-validated) manifest, and the <see cref="AssemblyLoadContext"/>
/// the plugin was loaded into. Returned by <see cref="PluginLoader"/> and
/// consumed by the platform's DI wiring.
/// </summary>
/// <remarks>
/// <see cref="Module"/> is typed as the base <see cref="IPluginModule"/>
/// because the loader handles both game plugins
/// (<see cref="IGameModule"/>) and library plugins
/// (<see cref="ILibraryModule"/>). Consumers that need game-specific behavior
/// should type-test with <c>is IGameModule</c>; the manifest's
/// <see cref="IPluginManifest.Kind"/> agrees with the runtime type.
/// </remarks>
/// <param name="Module">The activated plugin module (game or library).</param>
/// <param name="Manifest">Manifest parsed from <c>plugin.json</c>. Authoritative.</param>
/// <param name="Assembly">The plugin's primary assembly.</param>
/// <param name="LoadContext">
/// The per-plugin <see cref="AssemblyLoadContext"/>. Currently always
/// non-collectible; retained on this record so future unload/hot-reload work
/// has a handle without re-plumbing.
/// </param>
public sealed record LoadedPlugin(
    IPluginModule Module,
    IPluginManifest Manifest,
    Assembly Assembly,
    AssemblyLoadContext LoadContext);
