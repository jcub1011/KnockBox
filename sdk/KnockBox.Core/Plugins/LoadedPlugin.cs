using System.Reflection;
using System.Runtime.Loader;

namespace KnockBox.Core.Plugins;

/// <summary>
/// One successfully-loaded plugin: the <see cref="IGameModule"/> instance, its
/// (disk-validated) manifest, and the <see cref="AssemblyLoadContext"/> the
/// plugin was loaded into. Returned by <see cref="PluginLoader"/> and consumed
/// by the platform's DI wiring.
/// </summary>
/// <param name="Module">The activated game module.</param>
/// <param name="Manifest">Manifest parsed from <c>plugin.json</c>. Authoritative.</param>
/// <param name="Assembly">The plugin's primary assembly.</param>
/// <param name="LoadContext">
/// The per-plugin <see cref="AssemblyLoadContext"/>. Currently always
/// non-collectible; retained on this record so future unload/hot-reload work
/// has a handle without re-plumbing.
/// </param>
public sealed record LoadedPlugin(
    IGameModule Module,
    IPluginManifest Manifest,
    Assembly Assembly,
    AssemblyLoadContext LoadContext);
