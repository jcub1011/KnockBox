namespace KnockBox.Platform.Games;

/// <summary>
/// Wire shape for one game in the <c>GET /api/games</c> catalogue. Mirrors the
/// fields the Blazor Server home grid renders (name, description, route, tile, WIP)
/// plus <see cref="HasClientUi"/>, which tells the WASM home shell whether a game
/// can run in the browser yet (it ships a <c>.Client</c> assembly + integrity
/// hashes) or is still Server-only.
/// </summary>
/// <remarks>
/// Deliberately NOT a shared CLR type with the client's parallel
/// <c>KnockBox.Core.Client.Catalogue.GameCatalogueEntry</c>: the WASM client
/// references neither this assembly nor KnockBox.Core's manifest types. The two
/// records cross the wire as JSON only — the same boundary convention as
/// <c>ClientAssetEntry</c> / <c>ClientPluginManifest</c>.
/// </remarks>
public sealed record GameCatalogueEntry(
    string Name,
    string Description,
    string RouteIdentifier,
    string EntryAssembly,
    string? TileAsset,
    bool WorkInProgress,
    bool HasClientUi);
