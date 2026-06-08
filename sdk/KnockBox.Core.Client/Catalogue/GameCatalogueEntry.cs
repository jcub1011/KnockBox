namespace KnockBox.Core.Client.Catalogue;

/// <summary>
/// Browser-side shape of one game in the <c>GET /api/games</c> catalogue the WASM
/// home shell fetches. Parallel to the server's
/// <c>KnockBox.Platform.Games.GameCatalogueEntry</c> (identical JSON property
/// names) — they cross the wire as JSON, with no shared CLR type, matching the
/// <c>ClientPluginManifest</c> wire-boundary convention.
/// </summary>
public sealed record GameCatalogueEntry(
    string Name,
    string Description,
    string RouteIdentifier,
    string EntryAssembly,
    string? TileAsset,
    bool WorkInProgress,
    bool HasClientUi);
