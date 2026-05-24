# M01 — Display View Completion & Sharing

> **Goal**: take the existing `DndMapperDisplay.razor` scaffold from "functional stub" to "production-ready, discoverable, tested". Extract a `DisplayProjection` helper that centralizes "what the display sees" so M04 has an obvious extension point for fog-aware filtering. Add a host-side button that opens the display URL in a new tab and copies it to the clipboard. No fog work in M01.
>
> **Dependencies**: none. The platform observer-attach (`IGameRoomObserver`) is already wired in `sdk/KnockBox.Platform/Services/State/Shared/GameRoomObserver.cs` and registered via `StateRegistrations.cs`; tests already exist at `sdk/KnockBox.PlatformTests/Unit/GameRoomObserverTests.cs`.
>
> **GDD references**: §13 (display view design — anonymous URL, no GM panels, no hidden tokens, no hidden images, roll log gated by `RollsVisibleToPlayers`), §17.5 (display verification matrix).
>
> **Out of scope** (do NOT implement here): any fog work (M02–M04). Auto-retry on missing room (design decision: not doing this). Adding sheet panels or any interactivity to the display.

---

## 1. Context

`host/KnockBox.DndMapper/Pages/DndMapperDisplay.razor` already exists and compiles. It:
- Routes at `/room/dnd-mapper/{ObfuscatedRoomCode}/display`.
- Injects `IGameRoomObserver` and calls `Observer.Attach("dnd-mapper", ObfuscatedRoomCode)` in `OnInitialized`.
- Renders combat banner (top), SVG canvas (center) with images filtered by `!Hidden`, host markup, and tokens filtered by `!Hidden`, plus a roll-log aside when `Settings.RollsVisibleToPlayers` is true.
- Subscribes to `state.StateChangedEventManager` for re-render.
- Disposes both the subscription and the attachment in `Dispose`.

What's missing for production:
1. **CSS**: only three `dndm-display` references exist in `wwwroot/css/panels.css`; the page has no dedicated stylesheet. On a TV the SVG should fill `100vw × 100vh` with black letterbox, no scrollbars, no chrome. Today it inherits whatever ambient layout the body uses.
2. **Discovery**: no host UI affordance points at the display URL. The host has to construct it manually.
3. **Test coverage**: zero tests assert that hidden tokens / hidden images / non-shared rolls actually stay off the display. The rendering logic is buried inside the razor file and not unit-testable.
4. **Robustness of projection**: future fog work (M04) will need to filter tokens *and* images by fog. There's no obvious seam right now — the filtering lives inline as `.Where(t => !t.Hidden)` / `.Where(i => !i.Hidden)`. Extracting a `DisplayProjection` helper now means M04 has one obvious place to add a fog filter, and M01 also gets first-class test coverage.

---

## 2. Files to create / modify

### New files

```
host/KnockBox.DndMapper/Helpers/DisplayProjection.cs
host/KnockBox.DndMapper/Pages/DndMapperDisplay.razor.css
host/KnockBox.DndMapper/wwwroot/js/dndMapperClipboard.js     (only if no existing helper exists — verify first)
host/KnockBox.DndMapperTests/Unit/DisplayProjectionTests.cs
```

### Files to modify

- `host/KnockBox.DndMapper/Pages/DndMapperDisplay.razor` — call `DisplayProjection.Build(state)` once per render; iterate over the projected `Tokens` / `Images` / `RollLog` / `ActiveCombat` instead of state directly. Drop the inline `.Where` filters. Trim the `dndm-display`-prefixed inline styling from `panels.css` and rely on the new `.razor.css`.
- `host/KnockBox.DndMapper/wwwroot/css/panels.css` — remove the three `dndm-display`-class rules (they belong in the scoped stylesheet now).
- `host/KnockBox.DndMapper/Pages/DndMapperPlayingPhase.razor` (+ `.razor.cs`) — add a host-only "Open display view" button somewhere reasonable. Recommended location: the host control-bar area inside the canvas-area header (look for existing host floating actions like "End Session" and place near them). On click: call `NavigationManager.ToAbsoluteUri($"room/dnd-mapper/{RoomCode}/display")`, invoke `dndMapperClipboard.copy(url)`, then `window.open(url, "_blank")`. Show a small toast on success (`_toasts` cascading value is already in scope per line 13 of the existing razor).

### Files NOT touched in M01

- `Services/State/Games/Data/Map.cs`, `Token.cs`, `MapImage.cs` — display work doesn't change state shape.
- `Services/Logic/Games/DndMapperGameEngine.cs` — no new verbs.
- `plugin.json` — no capability changes.

---

## 3. Detailed work breakdown

### 3.1 `DisplayProjection` helper

`host/KnockBox.DndMapper/Helpers/DisplayProjection.cs`:

```csharp
namespace KnockBox.DndMapper.Helpers;

public sealed record DisplayProjection(
    Map? ActiveMap,
    IReadOnlyList<MapImage> VisibleImages,
    IReadOnlyList<Token> VisibleTokens,
    string? MarkupSvg,
    CombatState? ActiveCombat,
    IReadOnlyList<RollResult> VisibleRollLog)
{
    public static DisplayProjection Build(DndMapperGameState state)
    {
        var map = state.ActiveMapId is { } id
            ? state.Maps.FirstOrDefault(m => m.Id == id)
            : null;

        if (map is null)
            return new(null, [], [], null, state.ActiveCombat, []);

        var images = map.Images
            .Where(i => !i.Hidden)
            .OrderBy(i => i.LayerOrder)
            .ToArray();

        var tokens = map.Tokens
            .Where(t => !t.Hidden)
            .ToArray();

        var rolls = state.Settings.RollsVisibleToPlayers
            ? state.RollLog.TakeLast(10).Reverse().ToArray()
            : Array.Empty<RollResult>();

        return new(map, images, tokens, map.MarkupSvg, state.ActiveCombat, rolls);
    }
}
```

The helper is pure — no DI, no async. Trivially unit-testable. M04 will add fog filtering by passing the map's `FogMask` through the image/token filter calls.

### 3.2 `DndMapperDisplay.razor` rewrite

Replace the inline `.Where` filters with the projection. Key changes:

```razor
@code {
    private DisplayProjection _projection = new(null, [], [], null, null, []);

    private async ValueTask OnStateChanged()
    {
        if (_state is not null)
            _projection = DisplayProjection.Build(_state);
        await InvokeAsync(StateHasChanged);
    }

    protected override void OnInitialized()
    {
        var result = Observer.Attach("dnd-mapper", ObfuscatedRoomCode);
        if (!result.TryGetSuccess(out var attachment)) return;
        _attachment = attachment;
        if (attachment.State is not DndMapperGameState dndState) return;
        _state = dndState;
        _subscription = dndState.StateChangedEventManager.Subscribe(OnStateChanged);
        _projection = DisplayProjection.Build(dndState);
    }
}
```

Markup iterates `_projection.VisibleImages` / `_projection.VisibleTokens` / `_projection.VisibleRollLog` directly. The "Room not found" branch (`_state is null`) is unchanged.

### 3.3 `DndMapperDisplay.razor.css`

Scoped to `.dndm-display`. Goals:
- Full-viewport SVG, black background, no scrollbars: `body { overflow: hidden; }` — but scope it via `::deep` so it only applies when the display page is mounted. Recommended pattern: `.dndm-display { position: fixed; inset: 0; background: #000; }`.
- SVG fills the canvas area with `preserveAspectRatio="xMidYMid meet"` (already set in the razor) → black letterbox on either side falls out automatically.
- Combat banner: fixed top-center, semi-transparent dark background, large readable type (project visibility from across a room).
- Roll log aside: fixed top-right or bottom-right, narrow column, large readable type, subtle background.
- `dndm-display__empty` and `dndm-display--error`: centered, large.

Move (and rename if needed) the three rules from `panels.css` into this file.

### 3.4 Host "Open display view" button

In `DndMapperPlayingPhase.razor`, gate visibility on `IsHost`. Add the button alongside the existing host-only floating actions. The handler:

```csharp
private async Task OnOpenDisplayClicked()
{
    var url = NavigationManager.ToAbsoluteUri($"room/dnd-mapper/{RoomCode}/display").ToString();
    try
    {
        await JS.InvokeVoidAsync("import", "/_content/KnockBox.DndMapper/js/dndMapperClipboard.js");
        await JS.InvokeVoidAsync("dndMapperClipboard.copy", url);
        _toasts.Show("Display link copied to clipboard.");
    }
    catch
    {
        // Clipboard API can fail on http or restricted contexts; the new tab
        // still opens, so this is a soft failure.
    }
    await JS.InvokeVoidAsync("open", url, "_blank");
}
```

Verify before adding `dndMapperClipboard.js`: grep `wwwroot/js/` for any existing clipboard helper. If one exists, reuse it.

```javascript
// host/KnockBox.DndMapper/wwwroot/js/dndMapperClipboard.js
export const dndMapperClipboard = {
    copy(text) {
        if (navigator.clipboard?.writeText) {
            return navigator.clipboard.writeText(text);
        }
        return Promise.reject(new Error("Clipboard API unavailable."));
    }
};
window.dndMapperClipboard = dndMapperClipboard;
```

---

## 4. Tests

`host/KnockBox.DndMapperTests/Unit/DisplayProjectionTests.cs` — six tests minimum:

1. `Build_NoActiveMap_ReturnsEmpty` — `state.ActiveMapId = null` → projection has null map and empty lists.
2. `Build_FiltersHiddenTokens` — map with one visible + one hidden token → projection has only the visible one.
3. `Build_FiltersHiddenImages` — same shape for images.
4. `Build_OrdersImagesByLayerOrder` — three images with LayerOrder 2, 0, 1 → projection emits them in 0/1/2 order.
5. `Build_RollLogHiddenWhenSettingFalse` — `Settings.RollsVisibleToPlayers = false` and 5 rolls → `VisibleRollLog` is empty.
6. `Build_RollLogVisibleWhenSettingTrue_CapsAtTen` — 20 rolls, setting true → projection has the last 10 in reverse order.

These cover the entire "what the display sees" contract and become regression tests for M04.

No bUnit. The razor file is too thin and JS-coupled to test ergonomically; the projection helper carries all the testable logic.

---

## 5. Verification (manual)

1. Build and run the host: `dotnet run --project host/KnockBox/KnockBox.csproj`.
2. Create a DnD Mapper lobby as host. Start the game.
3. Click "Open display view" in the host control bar.
4. **Expected**: new tab opens at `https://localhost:.../room/dnd-mapper/{code}/display`. The clipboard contains the same absolute URL (verify by pasting into a third tab). A toast says "Display link copied to clipboard."
5. Display tab shows the active map, full screen, black letterbox. No host panels visible.
6. From the host tab, mark a token as hidden — within one SignalR round-trip, the token disappears from the display tab.
7. From the host tab, upload an image and toggle it hidden — verify it never appears on the display tab.
8. With `Settings.RollsVisibleToPlayers = false`, roll dice as host — display tab shows no roll log.
9. Toggle `RollsVisibleToPlayers = true` — display tab's roll log appears with the existing roll.
10. Open `https://localhost:.../room/dnd-mapper/INVALID/display` — display tab shows static "Room not found." with the invalid code rendered in the `<code>` element. No auto-retry.

Run `dotnet test host/KnockBox.Host.slnx` — all green including the new `DisplayProjectionTests`.
