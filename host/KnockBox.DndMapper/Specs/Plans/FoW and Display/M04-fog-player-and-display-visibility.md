# M04 — Fog of War: Player & Display Visibility

> **Goal**: players and the display view see fog as opaque (not the host's translucent overlay), and tokens / images behind fogged cells are filtered out at the data layer so a clever client can't peek by inspecting the DOM. The host's experience is unchanged.
>
> **Dependencies**: M02 (state + engine), M01 (`DisplayProjection` helper — the obvious place to extend), M03 (host fog UI — needed to *create* fog to verify against, not technically a code dependency).
>
> **GDD references**: §13 (display view visibility rules: hidden tokens / hidden images absent, not ghosted), §15 (fog feature description). The principle of "no host secret in the client payload" is the same one §13 uses for `Token.Hidden = true` — fog is treated identically.
>
> **Out of scope** (do NOT implement here): host UI (M03), end-to-end matrix (M05), any new engine verbs (M02 covers them).

---

## 1. Context

After M03, the host can fog cells and sees them as a translucent overlay. Players and the display view currently render fog as if it didn't exist (no overlay, no filtering). M04 closes the gap with three concerns:

1. **Player rendering**: fogged cells render as fully opaque black `<rect>` (no see-through, unlike the host's `fill-opacity="0.45"`). Player tabs use the same `MapCanvas.razor` as the host with the `IsHost` flag flipped — so the conditional render path is already in place from M03.
2. **Token filtering for non-hosts**: a token whose `(round(X), round(Y))` cell is fogged is removed from the player / display rendering pipeline before the SVG is generated. The host still sees those tokens.
3. **Image filtering for non-hosts**: a `MapImage` whose bounding box is **fully** covered by fog (every corner cell is fogged) is removed. If any corner is on a revealed cell, the image stays visible. This is a deliberately conservative rule — see §3.2 for why.

The visibility filters live in `Helpers/` as pure functions so they're trivially testable and reusable between the inline `MapCanvas` filter (for players) and `DisplayProjection.Build` (for the display).

---

## 2. Files to create / modify

### Files to modify

- `host/KnockBox.DndMapper/Helpers/TokenVisibilityFilter.cs` — add an overload that also drops tokens on fogged cells when the caller isn't the host.
- `host/KnockBox.DndMapper/Helpers/DisplayProjection.cs` — apply the new token + image fog filters when building a projection.
- `host/KnockBox.DndMapper/Pages/Components/MapCanvas.razor` — render fog as opaque (`fill-opacity="1"`) when `!IsHost`; filter tokens and images via the new helpers for player rendering.
- `host/KnockBox.DndMapper/Pages/DndMapperDisplay.razor` — render fog cells from the projection's active-map `FogMask` using the opaque style; tokens and images already come pre-filtered from the projection.

### New files

```
host/KnockBox.DndMapper/Helpers/ImageVisibilityFilter.cs
host/KnockBox.DndMapperTests/Unit/FogVisibilityTests.cs
```

### Files NOT touched in M04

- `Map.cs`, `Token.cs`, `MapImage.cs` — no model changes.
- `DndMapperGameEngine.cs` — no verb changes.
- `HostFogPanel.razor` — no changes.

---

## 3. Detailed work breakdown

### 3.1 `TokenVisibilityFilter.cs` extension

Current signature (per the explorer report):

```csharp
public static IEnumerable<Token> VisibleTokensFor(IEnumerable<Token> tokens, bool isHost)
    => isHost ? tokens : tokens.Where(t => !t.Hidden);
```

Add an overload that takes the map so the helper can consult the fog mask:

```csharp
public static IEnumerable<Token> VisibleTokensFor(IEnumerable<Token> tokens, Map map, bool isHost)
{
    if (isHost) return tokens;
    return tokens.Where(t =>
    {
        if (t.Hidden) return false;
        var cx = (int)Math.Floor(t.X);
        var cy = (int)Math.Floor(t.Y);
        return !map.IsFogged(cx, cy);
    });
}
```

Keep the existing two-arg overload alive (still used by code paths that don't have a map handy, e.g. token-list panels). New call sites prefer the three-arg form.

Cell-coordinate rule: a token at `(X, Y)` is in cell `(floor(X), floor(Y))`. Tokens are usually placed at integer cell centers (`X = cx + 0.5`), but the engine allows continuous positions — floor is the right rounding for "which cell does the center sit on".

### 3.2 `ImageVisibilityFilter.cs`

```csharp
namespace KnockBox.DndMapper.Helpers;

/// <summary>
/// Filters images for non-host viewers based on fog.
/// </summary>
/// <remarks>
/// Rule: an image is hidden from non-hosts when ALL FOUR CORNERS of its
/// bounding box fall on fogged cells. If any corner sits on a revealed cell,
/// the image stays visible — this is a deliberate "lean toward showing".
///
/// Why this rule and not "any corner fogged → hide"? Images are typically
/// background art (parchment, dungeon tile). Partial occlusion is the norm
/// during exploration — the player should see the half they've revealed.
/// Hiding the whole image on a single fogged corner would make exploration
/// feel like a strobe light. The conservative rule keeps reveals progressive.
///
/// Rotation is approximated: we use the AABB of the unrotated rectangle.
/// Edge case for heavily rotated images is acceptable in v1.
/// </remarks>
public static class ImageVisibilityFilter
{
    public static IEnumerable<MapImage> VisibleImagesFor(IEnumerable<MapImage> images, Map map, bool isHost)
    {
        if (isHost) return images;
        return images.Where(img =>
        {
            if (img.Hidden) return false;
            return AnyCornerRevealed(img, map);
        });
    }

    private static bool AnyCornerRevealed(MapImage img, Map map)
    {
        var x0 = (int)Math.Floor(img.X);
        var y0 = (int)Math.Floor(img.Y);
        var x1 = (int)Math.Floor(img.X + img.Width - 0.0001);
        var y1 = (int)Math.Floor(img.Y + img.Height - 0.0001);
        return !map.IsFogged(x0, y0)
            || !map.IsFogged(x1, y0)
            || !map.IsFogged(x0, y1)
            || !map.IsFogged(x1, y1);
    }
}
```

### 3.3 `DisplayProjection.Build` update

Inside `DisplayProjection.cs` (created in M01), change the projection to consult the filters:

```csharp
var images = ImageVisibilityFilter.VisibleImagesFor(map.Images, map, isHost: false)
    .OrderBy(i => i.LayerOrder)
    .ToArray();

var tokens = TokenVisibilityFilter.VisibleTokensFor(map.Tokens, map, isHost: false)
    .ToArray();
```

Also pass through the fog mask itself so the display page can render fog cells:

```csharp
public sealed record DisplayProjection(
    Map? ActiveMap,
    IReadOnlyList<MapImage> VisibleImages,
    IReadOnlyList<Token> VisibleTokens,
    string? MarkupSvg,
    CombatState? ActiveCombat,
    IReadOnlyList<RollResult> VisibleRollLog,
    IReadOnlyList<(int cx, int cy)> FoggedCells);
```

`FoggedCells` is enumerated once during `Build` so the razor doesn't loop the bitset on every re-render. On a 100×100 fully-fogged map this is 10,000 tuples — still cheap (one allocation per state change, not per render).

### 3.4 `MapCanvas.razor` player rendering

In the fog-overlay render block introduced in M03:

```razor
@if (_activeMap?.FogMask.Length > 0)
{
    <g class="@(IsHost ? "dndm-fog dndm-fog--host" : "dndm-fog dndm-fog--player")">
        @for (var cy = 0; cy < _activeMap.Grid.HeightCells; cy++)
        {
            for (var cx = 0; cx < _activeMap.Grid.WidthCells; cx++)
            {
                if (_activeMap.IsFogged(cx, cy))
                {
                    <rect x="@cx" y="@cy" width="1" height="1"
                          fill="#000" fill-opacity="@(IsHost ? "0.45" : "1")"
                          pointer-events="none" />
                }
            }
        }
    </g>
}
```

Token and image iteration uses the new three-arg overloads:

```razor
@foreach (var img in ImageVisibilityFilter.VisibleImagesFor(_activeMap.Images, _activeMap, IsHost).OrderBy(i => i.LayerOrder))
@foreach (var tok in TokenVisibilityFilter.VisibleTokensFor(_activeMap.Tokens, _activeMap, IsHost))
```

### 3.5 `DndMapperDisplay.razor` fog rendering

Iterate `_projection.FoggedCells`:

```razor
@if (_projection.FoggedCells.Count > 0)
{
    <g class="dndm-display__fog">
        @foreach (var (cx, cy) in _projection.FoggedCells)
        {
            <rect x="@cx" y="@cy" width="1" height="1" fill="#000" pointer-events="none" />
        }
    </g>
}
```

---

## 4. Tests

`host/KnockBox.DndMapperTests/Unit/FogVisibilityTests.cs`:

**Token filter:**
1. `Token_OnFoggedCell_NonHost_Filtered`.
2. `Token_OnFoggedCell_Host_Visible`.
3. `Token_OnRevealedCell_NonHost_Visible`.
4. `Token_Hidden_NonHost_FilteredEvenWhenCellRevealed`.
5. `Token_ContinuousCoords_BetweenCells_UsesFloorCell` — token at `(3.4, 5.6)` is filtered when cell `(3, 5)` is fogged, visible when `(3, 5)` is revealed even if `(4, 6)` is fogged.

**Image filter:**
6. `Image_AllCornersFogged_NonHost_Filtered`.
7. `Image_OneCornerRevealed_NonHost_Visible`.
8. `Image_Hidden_NonHost_FilteredEvenWithRevealedCorners`.
9. `Image_Host_AlwaysVisible_RegardlessOfFog`.

**Display projection integration:**
10. `DisplayProjection_AppliesFogFilters` — state with one fogged area, one token on it, one image fully on it, one image partial → projection drops the token + fully-fogged image, keeps the partial image.
11. `DisplayProjection_FoggedCellsPopulated` — `FoggedCells.Count` matches the number of true bits in the fog mask.

`DisplayProjectionTests` from M01 should continue to pass unchanged (no fog → empty `FoggedCells`, unfiltered tokens / images).

---

## 5. Verification (manual)

1. Build and run the host. Open three tabs: host, player, display.
2. Host paints a 3×3 fog square. Host tab: translucent overlay. Player + display tabs: opaque black square. No token / image leak through DOM inspection in the player / display tabs.
3. Host places an NPC token under the fog. Player + display tabs: token not present in the DOM (verified via dev tools — not just hidden via CSS).
4. Host erases one of the nine fog cells over the token. Player + display tabs: token appears.
5. Upload an image fully covered by fog. Player + display tabs: image not rendered.
6. Host erases one corner of the image's footprint. Player + display tabs: image now visible.
7. Host marks a non-fogged token as `Hidden = true`. Player + display tabs: token disappears.
8. Display URL → confirm fog renders identically to the player tab.

Run `dotnet test host/KnockBox.Host.slnx` — all green.
