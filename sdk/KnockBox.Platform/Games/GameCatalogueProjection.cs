using KnockBox.Core.Plugins;

namespace KnockBox.Platform.Games;

/// <summary>
/// Builds the <c>GET /api/games</c> catalogue from the loaded game modules. Applies
/// the SAME availability gate and name sort the Blazor Server home grid uses
/// (<c>Home.OnInitializedAsync</c> / <c>RebuildVisibleModules</c>), so the WASM
/// shell and the Server home show an identical list. Extracted from the endpoint
/// lambda so it is unit-testable.
/// </summary>
public static class GameCatalogueProjection
{
    public static IReadOnlyList<GameCatalogueEntry> Build(
        IEnumerable<IGameModule> modules,
        IGameAvailabilityService availability)
        => modules
            .Where(m => availability.IsEnabled(m.Manifest.RouteIdentifier))
            .OrderBy(m => m.Manifest.Name)
            .Select(m => new GameCatalogueEntry(
                m.Manifest.Name,
                m.Manifest.Description,
                m.Manifest.RouteIdentifier,
                m.Manifest.EntryAssembly,
                m.Manifest.TileAsset,
                m.Manifest.WorkInProgress,
                // A game can run on WASM only once it ships a client UI assembly AND
                // its build-time integrity hashes — belt-and-suspenders against a
                // half-authored manifest.
                HasClientUi: !string.IsNullOrEmpty(m.Manifest.ClientAssembly)
                             && m.Manifest.ClientAssets.Count > 0))
            .ToArray();
}
