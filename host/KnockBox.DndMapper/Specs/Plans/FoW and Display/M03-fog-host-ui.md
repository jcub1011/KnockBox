# M03 — Fog of War Host UI

> **Goal**: the host can paint, erase, fill, and clear fog from the map canvas. Fog renders to the host as a translucent overlay so it stays editable. Player and display rendering is unchanged in M03 — those are still showing fog as if it doesn't exist; M04 wires the opaque-and-filter behavior.
>
> **Dependencies**: M02 (engine verbs). Optional: M01 (display polish) — not on the critical path for M03.
>
> **GDD references**: §6 (grid cell math), §15 (fog feature description), patterns from `host/KnockBox.DndMapper/Pages/Components/MapCanvas.razor` (existing SVG canvas) and `host/KnockBox.DndMapper/Pages/Components/HostLayerPanel.razor` (existing host-only left-rail panel — pattern to mirror).
>
> **Out of scope** (do NOT implement here): player rendering changes, display rendering changes, visibility filtering (M04). End-to-end verification matrix (M05).

---

## 1. Context

After M02, the host can paint fog through engine calls but has no UI to invoke them. M03 adds:

1. A new left-rail `HostFogPanel` with: paint/erase mode toggle, brush radius (1 / 2 / 3 cells), "Fill with fog" and "Clear all fog" buttons (with confirm modal).
2. Fog-paint-mode wiring inside `MapCanvas.razor`: when active, mouse-down + drag accumulates touched cells (at the chosen brush radius) into a stroke buffer; mouse-up flushes the buffer via `engine.PaintFogAsync(mapId, cells, fogged: paintMode == Paint)`. Long strokes also flush every ~150ms to keep the UI responsive.
3. Host-only fog rendering inside `MapCanvas.razor`: a `<g class="dndm-fog">` layer drawn after markup and before tokens, rendering each fogged cell as a `<rect>` with `fill="#000" fill-opacity="0.45"` so the host can still see what's underneath while editing.

The screen-to-cell math is already solved for token drag in `wwwroot/js/dndMapperTokenDrag.js` and `wwwroot/js/dndMapperSvgMetrics.js`. The fog paint helper should be a *new, small* JS module that reuses the same viewBox arithmetic; do not try to graft it onto the token drag module (concerns are different — token drag tracks a single moving item, fog paint accumulates a set of cells).

---

## 2. Files to create / modify

### New files

```
host/KnockBox.DndMapper/Pages/Components/HostFogPanel.razor
host/KnockBox.DndMapper/Pages/Components/HostFogPanel.razor.cs
host/KnockBox.DndMapper/Pages/Components/HostFogPanel.razor.css
host/KnockBox.DndMapper/wwwroot/js/dndMapperFogPaint.js
```

### Files to modify

- `host/KnockBox.DndMapper/Pages/Components/MapCanvas.razor` (+ `.razor.cs`, `.razor.css`) — add the fog-paint mode plumbing and the host-only fog overlay layer.
- `host/KnockBox.DndMapper/Pages/DndMapperPlayingPhase.razor` — slot `<HostFogPanel State="State" />` into the host left rail between `<HostLayerPanel ... />` and `<HostInitiativePanel ... />` (line numbers in current file: after line 30, before line 31).

### Files NOT touched in M03

- Engine / state — already done in M02.
- Display page — M04.
- Player rendering — M04.

---

## 3. Detailed work breakdown

### 3.1 `HostFogPanel.razor`

Mirrors `HostLayerPanel.razor`'s structure. Cascading state injection, collapsible header, body with controls.

```razor
@using KnockBox.DndMapper.Services.State.Games
@inherits DisposableComponent

<section class="dndm-panel dndm-host-fog">
    <header class="dndm-panel__header">
        <h3>Fog of War</h3>
    </header>
    <div class="dndm-panel__body">
        <div class="dndm-host-fog__modes">
            <button class="@ModeButtonClass(FogPaintMode.Paint)"
                    @onclick="() => SetMode(FogPaintMode.Paint)">Paint</button>
            <button class="@ModeButtonClass(FogPaintMode.Erase)"
                    @onclick="() => SetMode(FogPaintMode.Erase)">Erase</button>
            <button class="@ModeButtonClass(FogPaintMode.Off)"
                    @onclick="() => SetMode(FogPaintMode.Off)">Off</button>
        </div>

        <div class="dndm-host-fog__brush">
            <label>Brush</label>
            @foreach (var r in new[] { 1, 2, 3 })
            {
                <button class="@(BrushRadius == r ? "is-active" : null)"
                        @onclick="() => SetBrush(r)">@r</button>
            }
        </div>

        <div class="dndm-host-fog__bulk">
            <button class="dndm-btn" @onclick="OnFillClicked">Fill with fog</button>
            <button class="dndm-btn dndm-btn--ghost" @onclick="OnClearClicked">Clear all fog</button>
        </div>
    </div>
</section>

@if (_confirmFill)
{
    <ConfirmModal Title="Fog the entire map?"
                  Message="Every cell will be hidden from players. You can erase areas afterward."
                  ConfirmLabel="Fill with fog"
                  OnConfirm="OnConfirmFill"
                  OnCancel="() => _confirmFill = false" />
}
@if (_confirmClear)
{
    <ConfirmModal Title="Reveal the entire map?"
                  Message="All fogged cells will be cleared. This can't be undone."
                  ConfirmLabel="Clear all fog"
                  OnConfirm="OnConfirmClear"
                  OnCancel="() => _confirmClear = false" />
}
```

`HostFogPanel.razor.cs` holds the state machine. It also publishes the chosen mode + brush radius to `MapCanvas` via a small shared service (`FogPaintMode`, `int BrushRadius`) — recommended approach: introduce a scoped `IFogPaintContext` service (interface + impl in `Services/Logic/Games/Visibility/`) that both `HostFogPanel` (writer) and `MapCanvas` (reader) consume. Razor cascading values would also work but a service is easier to test and avoids re-render coupling.

```csharp
public enum FogPaintMode { Off, Paint, Erase }

public interface IFogPaintContext
{
    FogPaintMode Mode { get; }
    int BrushRadius { get; }
    event Action? Changed;
    void Set(FogPaintMode mode, int brushRadius);
}
```

Register the implementation in `DndMapperModule.RegisterServices` as `AddScoped<IFogPaintContext, FogPaintContext>()`.

### 3.2 `MapCanvas.razor` changes

Three additions:

**(a) Host-only fog overlay layer.** Inside the SVG, after the markup `<g>` and before the token layer, when `IsHost`:

```razor
@if (IsHost && _activeMap?.FogMask.Length > 0)
{
    <g class="dndm-fog dndm-fog--host">
        @for (var cy = 0; cy < _activeMap.Grid.HeightCells; cy++)
        {
            for (var cx = 0; cx < _activeMap.Grid.WidthCells; cx++)
            {
                if (_activeMap.IsFogged(cx, cy))
                {
                    <rect x="@cx" y="@cy" width="1" height="1"
                          fill="#000" fill-opacity="0.45" pointer-events="none" />
                }
            }
        }
    </g>
}
```

For grids larger than ~50×50 the per-cell `<rect>` count climbs into the thousands and Blazor re-render becomes the bottleneck. If profiling shows jank, switch to a single SVG `<path>` whose `d` attribute concatenates a `M cx cy h1 v1 h-1 Z` segment per fogged cell. Defer that optimization to M05 if M03 ships without measurable jank at typical map sizes (≤ 40×40).

**(b) Fog-paint pointer events.** When `_fogContext.Mode != Off`, the canvas's pointer-event handlers (currently used for token drag and viewport pan) need to be re-routed. The cleanest pattern is a top-level pointer mode switch in `MapCanvas.razor.cs`:

```csharp
private async Task OnPointerDown(PointerEventArgs e)
{
    if (_fogContext.Mode != FogPaintMode.Off)
    {
        await _fogPaintModule.InvokeVoidAsync("dndMapperFogPaint.beginStroke", _svgRef, e.ClientX, e.ClientY);
        return;
    }
    // existing token-drag / pan handler ...
}
```

The JS module accumulates a `Set<string>` of `"cx,cy"` keys as the pointer moves and exposes them via a `[JSInvokable]` callback to flush a stroke (every 150ms or on `pointerup`).

**(c) Engine call from the stroke flush callback.**

```csharp
[JSInvokable]
public async Task FlushFogStroke(int[] xs, int[] ys)
{
    if (xs.Length == 0) return;
    var cells = new (int, int)[xs.Length];
    for (var i = 0; i < xs.Length; i++) cells[i] = (xs[i], ys[i]);
    var caller = UserService.CurrentUser!;
    await Engine.PaintFogAsync(State, caller, _activeMap!.Id, cells,
        fogged: _fogContext.Mode == FogPaintMode.Paint);
}
```

### 3.3 `dndMapperFogPaint.js`

A small module roughly modeled on `dndMapperTokenDrag.js` but with set-accumulation semantics:

```javascript
let stroke = null;

export const dndMapperFogPaint = {
    beginStroke(svgEl, dotnetRef, brushRadius) {
        const metrics = window.dndMapperSvgMetrics.from(svgEl);
        stroke = { cells: new Set(), brushRadius, metrics, dotnetRef, lastFlush: 0 };
        svgEl.addEventListener("pointermove", onMove);
        svgEl.addEventListener("pointerup", onUp, { once: true });
        svgEl.addEventListener("pointercancel", onUp, { once: true });
    }
};

function onMove(ev) {
    if (!stroke) return;
    const { cx, cy } = stroke.metrics.screenToCell(ev.clientX, ev.clientY);
    const r = stroke.brushRadius - 1;
    for (let dy = -r; dy <= r; dy++)
        for (let dx = -r; dx <= r; dx++)
            stroke.cells.add(`${cx + dx},${cy + dy}`);

    const now = performance.now();
    if (now - stroke.lastFlush > 150) {
        flush();
        stroke.lastFlush = now;
    }
}

function onUp(ev) {
    flush();
    stroke = null;
}

async function flush() {
    if (!stroke || stroke.cells.size === 0) return;
    const xs = [], ys = [];
    for (const key of stroke.cells) {
        const [cx, cy] = key.split(",").map(n => parseInt(n, 10));
        xs.push(cx); ys.push(cy);
    }
    stroke.cells.clear();
    await stroke.dotnetRef.invokeMethodAsync("FlushFogStroke", xs, ys);
}

window.dndMapperFogPaint = dndMapperFogPaint;
```

`window.dndMapperSvgMetrics.from(svgEl)` is the existing helper from `dndMapperSvgMetrics.js`; confirm its public shape during implementation and adapt if the actual export name differs.

---

## 4. Tests

bUnit on `HostFogPanel` is low-value (it's mostly buttons), so this milestone is verified primarily through M02's engine tests + M05's manual matrix.

One worthwhile unit test:

`host/KnockBox.DndMapperTests/Unit/FogPaintContextTests.cs`:
1. `Set_FiresChangedEvent`.
2. `BrushRadius_ClampedToValidRange` (1..3) — invalid values fall back to 1.

These keep the panel ↔ canvas plumbing honest. Everything else is covered by M02 engine tests (verb correctness) and M05 verification (visible behavior).

---

## 5. Verification (manual)

1. Build and run the host. Create a DnD Mapper lobby as host and start the game.
2. Confirm the new "Fog of War" panel appears in the host left rail between the Layer panel and the Initiative panel.
3. Click "Paint" mode. Brush size 1. Drag across a few cells on the canvas. Fogged cells appear with translucent black overlay; the underlying map / tokens / images stay visible to the host.
4. Switch to brush 3. Drag once — three cells per row across the drag path are fogged in one stroke.
5. Switch to "Erase" mode. Drag over fogged cells — they clear.
6. Click "Fill with fog" → confirm modal appears → click confirm → entire map is fogged (translucent black everywhere for the host).
7. Click "Clear all fog" → confirm modal → fog disappears.
8. Switch to "Off" mode. Verify normal pan / token-drag pointer behavior resumes.
9. Open a second browser as a player (or as the display via `/display` URL). Confirm the player / display tab is **unchanged** by the fog — fog is host-only-rendered at this milestone; M04 wires the player + display side.
