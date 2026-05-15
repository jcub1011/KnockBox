# M04 — Canvas, Grid, Tokens, Drag

> **Goal**: ship the playable VTT canvas. Phase-switch the room page, render the active map (images + grid + tokens), let the host and players drag tokens with permission checks, and animate remote moves with a ~150 ms tween. After M04 the game is *playable* end-to-end via the engine + canvas, even though all the management UI is still pending in M05.
>
> **Dependencies**: M01 (state model + engine verbs — especially `MoveTokenAsync` and `Map`/`Token` records), M02 (HTTP dispatcher — required because tokens need their *images* via `_content/...` only; map images via `/api/plugins/dnd-mapper/.../images/{id}` from M03), M03 (image serve endpoint, so the canvas can render uploaded background images).
>
> **GDD references**: §4.2 (per-map structure — rendering reference), §6 (grid — entire section: 6.1–6.4 incl. coordinate system & client-local pan/zoom), §7 (tokens — rendering, drag, types), §10 (real-time sync — what to broadcast, what's local), §13 (UI pages — `DndMapperRoom.razor`, `DndMapperPlayingPhase.razor`, `MapCanvas.razor`, `TokenLayer.razor`).
>
> **Out of scope** (do NOT implement here): host map switcher, image inspector, image upload UI, host token panel, permissions panel (all M05). Character sheet panel, dice modal, roll log panel (all M05). Lobby authoring, end-session button, full §17 verification matrix (M06). Display view (v1.x). Initiative banner (v1.x).

---

## 1. Context

After M01–M03, the engine fully drives a session: state model, all verbs, HTTP image upload/serve, and storage cleanup. But the existing `DndMapperLobby.razor` still shows only a lobby and a "Game scaffold — gameplay coming soon." placeholder once the game starts. M04 is where the game becomes visible and interactive.

This milestone adds the **canvas-level UX** — the SVG that renders maps and tokens, the drag interop that lets users move tokens, and the page-level wiring that picks the right phase component. It deliberately stops short of authoring panels (M05) and lobby polish (M06) so that the canvas work is a clean, isolated change set.

Reference patterns to mirror:
- `host/KnockBox.Spardle/Pages/SpardleRoom.razor` — phase-switch convention.
- `host/KnockBox.DrawnToDress/Pages/OutfitCustomizationPhase.razor.cs` — SVG drag interop, lazy-loaded JS module, `[JSInvokable]` callback shape.
- `host/KnockBox/wwwroot/js/outfitItemDrag.js` — viewBox-aware mouse + touch handling.

The DnD Mapper canvas is structurally similar to DrawnToDress (one SVG, draggable items) but:
- Many tokens (vs ~6 clothing items) — the JS needs to handle a dynamic, larger set keyed by token GUID.
- Pan / zoom are required (DrawnToDress fixes the viewBox).
- Hidden tokens have host-only rendering (eye-slash overlay) per §13 / O-10.
- Remote token moves animate via ~150 ms tween (O-6) — this is a pure client-side rendering concern, not a state shape concern.

---

## 2. Files to create / modify

### New files

```
host/KnockBox.DndMapper/Pages/DndMapperRoom.razor
host/KnockBox.DndMapper/Pages/DndMapperRoom.razor.cs
host/KnockBox.DndMapper/Pages/DndMapperPlayingPhase.razor
host/KnockBox.DndMapper/Pages/DndMapperPlayingPhase.razor.cs
host/KnockBox.DndMapper/Pages/Components/MapCanvas.razor
host/KnockBox.DndMapper/Pages/Components/MapCanvas.razor.cs
host/KnockBox.DndMapper/Pages/Components/MapCanvas.razor.css
host/KnockBox.DndMapper/Pages/Components/TokenLayer.razor
host/KnockBox.DndMapper/Pages/Components/TokenLayer.razor.cs
host/KnockBox.DndMapper/Pages/Components/TokenLayer.razor.css
host/KnockBox.DndMapper/wwwroot/js/dndMapperTokenDrag.js
```

### Files to modify

- `host/KnockBox.DndMapper/Pages/DndMapperLobby.razor` — remove the `@page "/room/dnd-mapper/{ObfuscatedRoomCode}"` directive (now hosted by `DndMapperRoom.razor`); the lobby stays a regular component invoked by `DndMapperRoom.razor` when `Phase == Lobby`.
- `host/KnockBox.DndMapper/Pages/DndMapperLobby.razor.cs` — drop the `[Parameter] public string ObfuscatedRoomCode` if it duplicates `DndMapperRoom`'s; pass-through if `LobbyPageBase` lives on `DndMapperRoom` and `DndMapperLobby` becomes a child component (recommended). See §3.1 design choice.
- `host/KnockBox.DndMapper/Pages/Components/DndMapperHeader.razor` — likely fine as-is; verify it still receives the right props from `DndMapperRoom`.

### Files NOT touched in M04

- `Services/State/Games/*` — no state shape changes.
- `Services/Logic/Games/DndMapperGameEngine.cs` — no new verbs (M01 + M03 are sufficient).
- `Services/Logic/Games/Http/*` — no HTTP changes.
- `plugin.json` — no capability changes.

---

## 3. Detailed work breakdown

### 3.1 `DndMapperRoom.razor` (new — page entry)

Template — mirrors `host/KnockBox.Spardle/Pages/SpardleRoom.razor`. Phase children receive `GameState` / `RoomCode` / `IsHost` / `CurrentUserId` as **explicit parameters**, matching Spardle's pattern (`SpardleRoom.razor` passes `State="GameState" RoomCode="@RoomCode"` to its children — no `<CascadingValue>` wrappers). Explicit props keep the dependency edges visible and avoid the gotcha where a deeply-nested child silently fails to receive a cascading value.

```razor
@page "/room/dnd-mapper/{ObfuscatedRoomCode}"
@inherits LobbyPageBase<DndMapperGameState>
@namespace KnockBox.DndMapper.Pages

<HeadContent>
    <link rel="stylesheet" href="_content/KnockBox.DndMapper/css/dnd-mapper.bundle.scp.css" />
</HeadContent>

@if (GameState is null || UserService.CurrentUser is null)
{
    <div class="dnd-loading">Loading…</div>
}
else
{
    @switch (GameState.Phase)
    {
        case DndMapperPhase.Lobby:
            <DndMapperLobby State="GameState" RoomCode="@RoomCode" />
            break;

        case DndMapperPhase.Playing:
            <DndMapperPlayingPhase State="GameState"
                                   RoomCode="@RoomCode"
                                   IsHost="IsHost()"
                                   CurrentUserId="@UserService.CurrentUser.Id" />
            break;
    }
}
```

Code-behind (`DndMapperRoom.razor.cs`):

- `[Inject] DndMapperGameEngine GameEngine` — concrete-engine injection (matches `SpardleRoom.razor.cs`'s convention).
- Override `OnLobbyInitializedAsync` if any one-time setup is needed; for M04 there isn't.
- Override `OnStateChangedAsync` to `await InvokeAsync(StateHasChanged)` — the base does this by default, but if any phase-transition cleanup is needed (e.g. dispose drag interop on Lobby → Playing), do it here.

> **Design choice — where does `LobbyPageBase` live?**
> - **Option A (chosen)**: `LobbyPageBase` on `DndMapperRoom`; `DndMapperLobby` and `DndMapperPlayingPhase` become regular `ComponentBase` children that receive explicit `[Parameter]` props (`State`, `RoomCode`, `IsHost`, `CurrentUserId`). Matches Spardle.
> - Option B: each phase component inherits `LobbyPageBase<DndMapperGameState>` independently and re-validates the URI. More boilerplate per phase; rejected.
>
> **Do not use `[CascadingParameter]`** in M04 unless the parent explicitly emits a `<CascadingValue>` — Spardle's pattern is explicit props, and matching it keeps wiring obvious.

### 3.2 `DndMapperLobby.razor` (existing — demote to child)

Drop the `@page` directive. Keep the rest of the markup. The component receives `State` and `RoomCode` as explicit `[Parameter]` props from `DndMapperRoom` (matching the parent's render snippet in §3.1). Confirm `DndMapperLobby.razor.cs` still injects what it needs (e.g. `GameEngine`, `IUserService`).

### 3.3 `DndMapperPlayingPhase.razor` (new — playing-phase container)

For M04 this is intentionally minimal — just the canvas. M05 fills in the panels around it.

```razor
@inherits DisposableComponent
@implements IAsyncDisposable
@namespace KnockBox.DndMapper.Pages

<div class="dnd-mapper-playing">
    <MapCanvas State="State"
               RoomCode="@RoomCode"
               Map="ActiveMap"
               IsHost="IsHost"
               CurrentUserId="@CurrentUserId" />
</div>

@code {
    [Parameter] public DndMapperGameState State { get; set; } = default!;
    [Parameter] public string RoomCode { get; set; } = "";
    [Parameter] public bool IsHost { get; set; }
    [Parameter] public string CurrentUserId { get; set; } = "";

    private Map? ActiveMap =>
        State.ActiveMapId is Guid id
            ? State.Maps.FirstOrDefault(m => m.Id == id)
            : null;
}
```

If `ActiveMap` is null, `MapCanvas` renders an empty placeholder ("No active map.").

### 3.4 `MapCanvas.razor` (new — SVG renderer + pan/zoom)

Responsibilities:
- Render the grid (using `Map.Grid.WidthCells × HeightCells`, `CellPixels`, `LineColor`).
- Render image layers in `LayerOrder` ascending (lowest first). Image src is `/api/plugins/dnd-mapper/{ObfuscatedRoomCode}/images/{image.Id}`.
- Render `TokenLayer` on top of images.
- Handle wheel-zoom and drag-pan (only when not hitting a token — events bubble up from `TokenLayer` and are consumed there for token drags).
- Compute viewBox from `(panX, panY, zoom, viewportWidth, viewportHeight)`.

Key design points:

- **Coordinate system** (per §6.3): map space units = grid cells. The SVG `viewBox` uses *cell units*; the renderer doesn't multiply by `CellPixels` for token positions (tokens store fractional cell coordinates). The visual size of each cell on screen depends on the SVG's actual pixel dimensions and the viewBox — `CellPixels` is the "natural size at zoom 1.0" used to size the SVG element itself when zoom is at the default.
- **Pan / zoom state** (per §6.4): purely client-local — held in component state, never broadcast. Wheel events compute new zoom (clamp 0.25–4.0); space+drag pans; or drag-pan triggered when the mousedown isn't on a token.
- **`ShowGridLines`** (per §6.2): each player has a local toggle that overrides the host's setting. Implement with a parameter `bool LocalShowGridLines` defaulting to `Map.Grid.ShowGridLines`; flip via a small toolbar (M05 may move that toolbar into a polished spot, but a basic checkbox is fine here).

```razor
@inherits DisposableComponent
@inject IJSRuntime JSRuntime
@namespace KnockBox.DndMapper.Pages.Components

<svg @ref="_svgElement"
     class="dnd-map-canvas"
     viewBox="@($"{_panX} {_panY} {_viewW} {_viewH}")"
     @onwheel="OnWheel"
     @onmousedown="OnMouseDown"
     @onmousemove="OnMouseMove"
     @onmouseup="OnMouseUp"
     preserveAspectRatio="xMidYMid meet">

    <!-- Grid lines -->
    @if (LocalShowGridLines && Map is not null)
    {
        <g class="grid-layer">
            @for (int x = 0; x <= Map.Grid.WidthCells; x++)
            {
                <line x1="@x" y1="0" x2="@x" y2="@Map.Grid.HeightCells"
                      stroke="@Map.Grid.LineColor" stroke-width="0.02" />
            }
            @for (int y = 0; y <= Map.Grid.HeightCells; y++)
            {
                <line x1="0" y1="@y" x2="@Map.Grid.WidthCells" y2="@y"
                      stroke="@Map.Grid.LineColor" stroke-width="0.02" />
            }
        </g>
    }

    <!-- Image layers, sorted ascending. RoomCode is the obfuscated URI segment -->
    <!-- (e.g. {guidA}-{guidB}); the GET endpoint is anonymous (M02), so the browser -->
    <!-- can fetch directly without any cookie or auth header. -->
    @if (Map is not null)
    {
        foreach (var img in Map.Images.OrderBy(i => i.LayerOrder))
        {
            <image @key="img.Id"
                   href="@($"/api/plugins/dnd-mapper/{RoomCode}/images/{img.Id}")"
                   x="@img.X" y="@img.Y"
                   width="@img.Width" height="@img.Height"
                   transform="@($"rotate({img.Rotation} {img.X + img.Width / 2} {img.Y + img.Height / 2})")"
                   opacity="@img.Opacity" />
        }
    }

    <!-- Tokens on top -->
    <TokenLayer State="State"
                Map="Map"
                IsHost="IsHost"
                CurrentUserId="CurrentUserId"
                CellSize="1.0" />
</svg>
```

Code-behind handles pan/zoom math, wheel scaling, and exposes refs for `TokenLayer`.

### 3.5 `TokenLayer.razor` (new — interactive token rendering + drag)

Responsibilities:
- Render every visible token in the current map's `Tokens` as an SVG `<g>` element with a circle (border = `Token.Color`) and either an initial-letter `<text>` or a solid filled disc per `Token.IconKind`.
- Filter out hidden tokens for non-host viewers (`Token.Hidden && !IsHost` → not rendered at all).
- For host viewers, render hidden tokens at 50% opacity with an eye-slash overlay (per §13 / O-10).
- Wire up the JS drag interop. On drag-end, the JS calls `[JSInvokable] OnTokenMoved(string tokenId, double x, double y)` on this component.
- Apply ~150 ms tween animation when a token's `(X, Y)` updates from a remote state change (per §10 / O-6).

```razor
@inherits DisposableComponent
@inject IJSRuntime JSRuntime
@inject DndMapperGameEngine GameEngine
@inject IUserService UserService
@implements IAsyncDisposable
@namespace KnockBox.DndMapper.Pages.Components

<g class="token-layer" @ref="_layerRef">
    @if (Map is not null)
    {
        foreach (var token in VisibleTokens)
        {
            var pos = ResolvePosition(token); // tween-aware
            <g @key="token.Id"
               class="@TokenCssClass(token)" data-token-id="@token.Id"
               transform="@($"translate({pos.X} {pos.Y})")">
                <circle r="0.45" fill="@FillColor(token)" stroke="@token.Color" stroke-width="0.06" />
                @if (token.IconKind == TokenIconKind.Initial && !string.IsNullOrEmpty(token.Name))
                {
                    <text text-anchor="middle" dominant-baseline="central"
                          font-size="0.5" font-weight="bold" fill="@TextColor(token)">
                        @token.Name[..1].ToUpperInvariant()
                    </text>
                }
                @if (IsHost && token.Hidden)
                {
                    <!-- eye-slash overlay -->
                    <line x1="-0.4" y1="-0.4" x2="0.4" y2="0.4"
                          stroke="#ff4444" stroke-width="0.08" />
                }
            </g>
        }
    }
</g>
```

Key methods in `TokenLayer.razor.cs`:

- `IEnumerable<Token> VisibleTokens` — filters out hidden tokens for non-host viewers.
- `string TokenCssClass(Token t)` — `"token"` plus `"token--hidden"` if hidden (host-only render path).
- `string FillColor(Token t)` / `string TextColor(Token t)` — derive contrasting fill from `Token.Color` (e.g. fill = light tint of color, text = dark).
- `Position ResolvePosition(Token t)` — returns either `(t.X, t.Y)` if no tween in progress, or interpolated mid-tween position. Tween state lives in a `Dictionary<Guid, TokenTween>` keyed by `Token.Id`. A `TokenTween` records `(double FromX, double FromY, double ToX, double ToY, DateTime StartUtc)`. On state-change, compare to last-rendered position; if it changed, start a tween.
- `OnAfterRenderAsync(firstRender)` — on first render, lazily import `./js/dndMapperTokenDrag.js` and call `init` with the layer ref + DotNetObjectReference + viewBox dimensions.
- `[JSInvokable] OnTokenDragEnd(string tokenId, double x, double y)` — snap to grid if `Map.Grid.SnapToGrid` (round to nearest 0.5 cell offset so tokens center on cells), then call `engine.MoveTokenAsync(state, currentUser, tokenId, x, y)`. Surface failure (e.g. permission rejection) as a transient toast or a brief visual shake (M05 polishes this; M04 can log + revert visually).

> **Tween implementation (CSS-transition based — chosen approach)**:
>
> - Subscribe to `state.StateChangedEventManager` (already done by `LobbyPageBase` for the page; this child component subscribes via `OnInitializedAsync`).
> - On state change, just `await InvokeAsync(StateHasChanged)`. The SVG re-renders with the new `transform="translate(x y)"` attribute.
> - The `<g>` element carries `transition: transform 150ms ease-out;` (in `TokenLayer.razor.css`). The browser interpolates between the old and new `transform` over 150 ms.
> - **`@key="token.Id"` on the foreach is mandatory** — without it, Blazor's diff may re-create the `<g>` element on each render (especially if list ordering shifts), which destroys the from-state and short-circuits the CSS transition. With `@key`, the same DOM node is reused across renders and the transition fires correctly.
> - **No JS tick loop, no per-frame Blazor re-renders** — the browser does the interpolation. Render cost = 1 SVG diff per state change, not 60 per second.
> - **Fallback if a browser shows uneven SVG-attribute transitions**: switch to a JS `requestAnimationFrame` loop that interpolates `_tweens` over 150 ms and writes the `transform` attribute via JSInterop. Adds complexity; only adopt if measurement shows the CSS approach janks.

### 3.6 `dndMapperTokenDrag.js` (new — drag interop)

Decision: **start by copying `host/KnockBox/wwwroot/js/outfitItemDrag.js`** into `host/KnockBox.DndMapper/wwwroot/js/dndMapperTokenDrag.js` and adapt. The DnD-Mapper case differs in:
- Tokens are keyed by `Guid`, not `ClothingType`.
- The set of items is dynamic (host can spawn / remove).
- The drag callback fires `OnTokenDragEnd(tokenId, x, y)` instead of `OnItemMoved`.
- Coordinates are in *cell units* (the SVG's viewBox), so the existing `getScreenCTM().inverse()` conversion already produces the right values.

API surface:

```js
export function initialize(svgId, dotNetRef, tokens, viewBoxWidth, viewBoxHeight) { ... }
export function setMovableTokens(svgId, tokens) { ... }   // re-publishes the movable token id list
export function dispose(svgId) { ... }
```

Where `tokens` is an array of `{ tokenId: string, movable: bool }`. The drag handler short-circuits if `movable === false` (so client-side prevents drag attempts the engine would reject anyway — the engine still re-validates per §7.3 step 3).

Touch + mouse, viewBox-aware via `getScreenCTM().inverse()`. On `mouseup` / `touchend`, call `dotNetRef.invokeMethodAsync('OnTokenDragEnd', tokenId, x, y)`.

> **`movable` calculation**: the .NET side computes per-token movability from `state.Settings.TokenMovement` + `Token.OwnerUserId` + `currentUser.Id` and passes the result to JS on every state change. Saves a server round-trip for clearly-rejected drags.

### 3.7 Subscriptions and disposal

`TokenLayer.razor.cs`:

```csharp
protected override void OnInitialized()
{
    _stateSub = State.StateChangedEventManager.Subscribe(OnStateChangedAsync);
}

private async ValueTask OnStateChangedAsync()
{
    // Compute movable token list, push to JS via setMovableTokens.
    if (_jsModule is not null)
    {
        var tokens = VisibleTokens.Select(t => new { tokenId = t.Id.ToString(), movable = CanMove(t) }).ToArray();
        await _jsModule.InvokeVoidAsync("setMovableTokens", _svgId, tokens);
    }
    await InvokeAsync(StateHasChanged);
}

public async ValueTask DisposeAsync()
{
    _stateSub?.Dispose();
    if (_jsModule is not null) await _jsModule.InvokeVoidAsync("dispose", _svgId);
    if (_dotNetRef is not null) _dotNetRef.Dispose();
    if (_jsModule is not null) await _jsModule.DisposeAsync();
}
```

`MapCanvas.razor.cs` does its own `IAsyncDisposable` for any module references it holds (likely none — pan/zoom is pure C#).

### 3.8 Pan / zoom math

Pan stored as `(_panX, _panY)` in cell units. Zoom stored as `_zoom : double` (1.0 = default, 0.25–4.0 clamped). ViewBox is `viewBox="{_panX} {_panY} {_viewW / _zoom} {_viewH / _zoom}"`.

Wheel-zoom centers on the cursor position: compute the cell-space coordinates under the cursor before the zoom change, apply the zoom, then adjust pan so the same cell stays under the cursor.

Drag-pan: `mousedown` on the SVG (not on a token) sets `_panning = true` and records the mouse position; `mousemove` while panning updates `_panX`, `_panY` proportionally; `mouseup` clears.

Don't broadcast pan/zoom — they're per-client state (§6.4 / §10).

### 3.9 Hidden token rendering rules

- Non-host viewers: hidden tokens are filtered out of `VisibleTokens`. They don't render at all — no eye-slash, no ghost, nothing. (Non-host clients shouldn't even know they exist.)
- Host viewer: hidden tokens render at 50% opacity (`opacity: 0.5` on the `<g>`) plus an eye-slash overlay (a diagonal line through the token, or a small icon — pick whatever is visually unambiguous). This is the GDD §13 / O-10 spec.

---

## 4. Acceptance criteria

- [ ] `dotnet build host/KnockBox.Host.slnx` succeeds; the plugin stages.
- [ ] M01–M03 tests still green.
- [ ] No new unit tests required for M04 (component-level behavior is covered manually + via the engine tests in M01); however, any pure helpers extracted (e.g. a `TokenColorHelper.GetTextColor(string borderHex)`) should have unit tests.
- [ ] Clicking the room URL navigates to `DndMapperRoom`, which switches between `DndMapperLobby` and `DndMapperPlayingPhase` based on `state.Phase`.
- [ ] After `StartAsync`, the playing phase shows the active map's grid, image layers (if any), and tokens.
- [ ] Pan + zoom work locally and don't broadcast.
- [ ] Token drag works: owner / host can drag own / any token under default permission; non-owner / non-host cannot. Engine permission check is re-enforced server-side.
- [ ] Remote token moves animate smoothly over ~150 ms.
- [ ] Hidden tokens are invisible to non-host viewers and visible-with-eye-slash to host.

---

## 5. Manual verification

Two-browser end-to-end:

1. `dotnet run --project host/KnockBox/KnockBox.csproj`. Open browser A, create room (host). Open browser B, join (player).
2. From browser A, programmatically pre-create a map (until M05/M06 ship the map-creation UI, you may need a debug verb or seeded state). Upload an image via M03's `curl` flow. Set the active map. Start the game.
3. Both browsers navigate to the playing-phase. Confirm:
   - Grid visible, image layer visible, both player and host tokens visible.
   - Player A (host) drags own token — both browsers see the move (with tween on browser B).
   - Player B drags own token — both browsers see the move.
   - Player B tries to drag host's NPC token — rejected (no movement on either client).
   - Pan + zoom work on each browser independently (the other's view doesn't change).
4. Host changes a token's `Hidden = true` (via debug verb or M05's panel later). Browser A still sees it (eye-slash). Browser B doesn't see it.
5. Host changes `state.Settings.TokenMovement = Anyone` (via debug verb or M05's panel later). Player B can now drag the NPC.

> If creating maps / uploading images via curl is too cumbersome, defer this scenario to M05/M06 once the host UI exists. M04 sign-off can be a smaller smoke test: verify the page renders, phase-switches, and any pre-seeded token drags correctly with permission enforcement.

---

## 6. Inline unit test plan

M04 is largely UI / interop work and the project doesn't currently use bUnit. Test what's testable:

- `Helpers/TokenVisibilityFilter.cs` (extract pure logic): `IEnumerable<Token> VisibleTokensFor(IEnumerable<Token> all, bool isHost)` — filters out `Hidden` for non-host.
  - `VisibleTokensFor_NonHostExcludesHidden`.
  - `VisibleTokensFor_HostIncludesHidden`.
  - `VisibleTokensFor_NonHiddenAlwaysIncluded`.
- `Helpers/TokenMovabilityResolver.cs`: `bool CanMove(Token, currentUserId, isHost, TokenMovementPolicy)`.
  - `CanMove_OwnerOrHost_OwnerOfPlayerToken_True`.
  - `CanMove_OwnerOrHost_HostOfAnyToken_True`.
  - `CanMove_OwnerOrHost_NonOwnerNonHost_False`.
  - `CanMove_Anyone_AnyParticipant_True`.
  - `CanMove_HostOnly_HostTrue_PlayerFalse`.
  - These mirror the engine-side permission logic in M01's `MoveTokenAsync` and the JS-side `movable` flag.
- (Optional) `Helpers/SnapToGridHelper.cs`: snap `(double x, double y)` to nearest cell center.
  - `Snap_AlreadyOnCenter_NoChange`.
  - `Snap_QuarterCellOffset_RoundsToNearest`.
  - `Snap_OutOfBounds_ClampsToValidRange` — depends on the snapping policy; document and test.

These extractions also benefit M01's engine-side validation (the engine could reuse `TokenMovabilityResolver` instead of duplicating the table). Refactor opportunity — flag during PR.

---

## 7. Implementation choices to flag during PR

- **CSS transition vs JS tween loop**: M04 picks CSS transitions on the `<g transform="...">` for simplicity. If browser SVG transition support proves uneven (some browsers don't smoothly animate `transform` attribute changes on SVG), fall back to the JS tick loop.
- **Local grid-line toggle**: M04 adds a small in-canvas checkbox or button. M05 may move this into a polished toolbar.
- **JS module copy vs reuse**: M04 picks copy-and-adapt over import-from-host. The host's `outfitItemDrag.js` has different semantics (DrawnToDress's clothing types vs DnD's dynamic token list) and forking is cleaner than parameterizing.
- **Touch zoom (pinch)**: out of scope for M04 — desktop-first per §15 ("Mobile-optimized layout — desktop-first"). Wheel-only zoom is fine for v1.
- **Spinner / loading state during state.Execute lock contention**: not needed — verbs are short. If the canvas appears to "freeze" during host bulk operations (image upload + transform), revisit in M05.
- **Cascading vs explicit parameters**: M04 picks **explicit `[Parameter]` props** on `DndMapperPlayingPhase`, `MapCanvas`, and `TokenLayer`, matching `SpardleRoom.razor`. Earlier draft used `[CascadingParameter]` without emitting a `<CascadingValue>` wrapper from `DndMapperRoom`; that would silently fail to inject. Explicit props make the wiring obvious at the call site.
- **`@key="token.Id"` and `@key="img.Id"`** are mandatory in the token loop and image loop respectively. Without them, Blazor's diff may re-mount SVG nodes when the underlying list reorders — which kills the CSS transition mid-animation for tokens, and re-fetches images unnecessarily. The cost of `@key` is one identity comparison per element per render; trivial.
