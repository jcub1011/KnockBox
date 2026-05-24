namespace KnockBox.Core.Plugins;

/// <summary>
/// Marker interface for a non-user-facing library plugin. The host loads every
/// <see cref="ILibraryModule"/> before any <see cref="IGameModule"/>, registers
/// its services into the global DI container, and protects those services from
/// shadowing by subsequently-loaded plugins. Library plugins have no route on
/// the home page and register zero game engines.
/// </summary>
/// <remarks>
/// <para>A library plugin pairs with a sibling <i>contracts</i> assembly that
/// holds the public interfaces (e.g. <c>KnockBox.WordService.Contracts</c> ships
/// alongside <c>KnockBox.WordService</c>). The library manifest declares the
/// contracts assembly's simple name in <see cref="IPluginManifest.ExportedContracts"/>,
/// and the loader promotes that DLL into the default
/// <see cref="System.Runtime.Loader.AssemblyLoadContext"/> at startup so every
/// consuming plugin sees an identical CLR type for the contract interfaces.
/// Without that promotion two plugins in separate ALCs would each load their
/// own copy of the contracts and DI would see them as different types.</para>
/// <para>Library plugins use strict <c>Major.Minor.Patch</c> SemVer. When two
/// folders ship the same library at the same Major.Minor with different Patch,
/// the highest Patch wins. Different Major or Minor → both versions load
/// side-by-side; consumer plugins bind to a specific version via their
/// compiled metadata reference.</para>
/// </remarks>
public interface ILibraryModule : IPluginModule
{
}
