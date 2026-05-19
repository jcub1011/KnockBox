using KnockBox.Core.Plugins;
using KnockBox.WordService.Contracts;
using KnockBox.WordService.Services;

namespace KnockBox.WordService;

/// <summary>
/// Entry point for the KnockBox.WordService library plugin. Registers the
/// singleton <see cref="IWordListService"/> implementation so consuming game
/// plugins (Spardle, and others to come) can constructor-inject it.
/// </summary>
/// <remarks>
/// This is a library plugin: it has no game engine and no home-page tile.
/// The host loads it before any game plugin, so by the time a game plugin's
/// <c>RegisterServices</c> runs, <see cref="IWordListService"/> is already in
/// the DI container.
/// </remarks>
public sealed class WordServiceModule : ILibraryModule
{
    public IPluginManifest Manifest { get; } =
        PluginManifest.FromEmbeddedResourceOrThrow(typeof(WordServiceModule).Assembly);

    public void RegisterServices(IPluginRegistration registration)
    {
        registration.AddSingleton<IWordListService, WordListService>();
    }
}
