# M02 — Fog of War State Model & Engine Verbs

> **Goal**: add the per-cell fog mask to `Map`, write host-only engine verbs (`PaintFogAsync` / `FillMapWithFogAsync` / `ClearAllFogAsync` / `RevealCellsAsync` / `HideCellsAsync`), and persist the mask through `DndMapperLibraryService`. After M02, fog can be driven end-to-end by tests; no rendering work yet.
>
> **Dependencies**: M01 is recommended (gets `DisplayProjection` in place so M04 has a clean extension point), but M02 doesn't actually depend on it — the engine layer is independent. SDK contract: none required.
>
> **GDD references**: §15 (fog-of-war listed as out-of-MVP — this milestone reverses that), §6 (grid coordinate system — cells are indexed by `(cx, cy)` with `0 ≤ cx < Grid.WidthCells` and `0 ≤ cy < Grid.HeightCells`).
>
> **Out of scope** (do NOT implement here): any rendering, any UI, any visibility filtering for players (M04). Brush-size / paint-stroke ergonomics (M03). The engine accepts a flat list of cells per call — that's the entire mutation surface.

---

## 1. Context

`Map` currently holds `Grid`, `Images`, `Tokens`, `DefaultSpawnPosition`, and `MarkupSvg`. There is no per-cell visibility data. Fog needs:

- One bit of state per cell, stored on the `Map` (not on `Token` / `MapImage` — those are placed in continuous space; fog lives in cell space).
- Engine verbs to mutate the mask in bulk. The UI (M03) will accumulate cells touched by a paint stroke client-side and flush them through one verb call per stroke (or every ~150ms if the stroke is long), so the verb's natural shape is "toggle a list of cells to a given value".
- Persistence through `DndMapperLibraryService` so a saved campaign retains its fog state.

**Storage shape decision**: packed `byte[]` (row-major bitset, length = `(WidthCells * HeightCells + 7) / 8`), exposed via instance helpers `IsFogged(int cx, int cy)` and `SetFogged(int cx, int cy, bool fogged)` on `Map`. An empty array means "all cells revealed" — engine verbs allocate on first paint, and `ClearAllFogAsync` resets to `[]` so the common case stays cheap.

A 100×100 grid is 10,000 bits = 1,250 bytes. Even a 200×200 grid is 5 KB. We are nowhere near a payload concern; the bitset is purely an ergonomic and serialization choice.

**Permission model**: fog is host-only. Use the same caller-vs-host check existing map verbs use. Verify the exact helper name during implementation (`IsHost(state, caller)` is the agent-reported signature, but confirm by reading `DndMapperGameEngine.cs`).

---

## 2. Files to create / modify

### Files to modify

- `host/KnockBox.DndMapper/Services/State/Games/Data/Map.cs` — add `FogMask` property and `IsFogged` / `SetFogged` helpers.
- `host/KnockBox.DndMapper/Services/Logic/Games/DndMapperGameEngine.cs` — add the five new verbs at the end of the existing map-verb region.
- `host/KnockBox.DndMapper/Services/Library/DndMapperLibraryService.cs` — include `FogMask` in the per-map save snapshot and load it back on hydrate.

### New files

```
host/KnockBox.DndMapperTests/Unit/FogOfWarVerbsTests.cs
host/KnockBox.DndMapperTests/Unit/FogMaskTests.cs              (unit-tests Map.IsFogged / Map.SetFogged in isolation)
```

### Files NOT touched in M02

- Any razor / component / CSS file.
- `Token.cs` / `MapImage.cs` — fog visibility doesn't change these models.
- `DndMapperGameState.cs` — no new state fields; fog lives on `Map`.

---

## 3. Detailed work breakdown

### 3.1 `Map.cs` changes

```csharp
namespace KnockBox.DndMapper.Services.State.Games.Data;

public sealed class Map
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public GridConfig Grid { get; set; } = new();
    public List<MapImage> Images { get; } = [];
    public List<Token> Tokens { get; } = [];
    public DateTime CreatedUtc { get; set; }
    public int ListOrder { get; set; }
    public (double X, double Y)? DefaultSpawnPosition { get; set; }
    public string? MarkupSvg { get; set; }

    // Packed row-major bitset, length = (WidthCells * HeightCells + 7) / 8.
    // Empty array = all cells revealed. Engine verbs allocate on first paint
    // and reset to [] on ClearAllFogAsync.
    public byte[] FogMask { get; set; } = [];

    public bool IsFogged(int cx, int cy)
    {
        if (FogMask.Length == 0) return false;
        if (cx < 0 || cy < 0 || cx >= Grid.WidthCells || cy >= Grid.HeightCells) return false;
        var bit = cy * Grid.WidthCells + cx;
        return (FogMask[bit >> 3] & (1 << (bit & 7))) != 0;
    }

    public void SetFogged(int cx, int cy, bool fogged)
    {
        if (cx < 0 || cy < 0 || cx >= Grid.WidthCells || cy >= Grid.HeightCells) return;
        EnsureMaskAllocated();
        var bit = cy * Grid.WidthCells + cx;
        var idx = bit >> 3;
        var mask = (byte)(1 << (bit & 7));
        if (fogged) FogMask[idx] |= mask;
        else        FogMask[idx] &= (byte)~mask;
    }

    private void EnsureMaskAllocated()
    {
        if (FogMask.Length != 0) return;
        var bytes = (Grid.WidthCells * Grid.HeightCells + 7) / 8;
        FogMask = new byte[bytes];
    }
}
```

The setter is public because the library service needs to assign a `byte[]` directly during deserialization. The verb layer is the only mutation path during runtime (engine verbs call `SetFogged` inside `state.Execute`).

### 3.2 Engine verbs

Add to `DndMapperGameEngine.cs` after the existing map-verb region. Signature pattern matches existing map verbs:

```csharp
public async Task<Result> PaintFogAsync(
    DndMapperGameState state,
    User caller,
    Guid mapId,
    IReadOnlyList<(int cx, int cy)> cells,
    bool fogged)
{
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(caller);
    ArgumentNullException.ThrowIfNull(cells);
    if (!IsHost(state, caller))
        return Result.FromError("Only the host can paint fog.");
    if (cells.Count == 0)
        return Result.Success;

    string? error = null;
    await state.ExecuteAsync(() =>
    {
        var map = state.Maps.FirstOrDefault(m => m.Id == mapId);
        if (map is null) { error = "Map not found."; return ValueTask.CompletedTask; }
        foreach (var (cx, cy) in cells)
            map.SetFogged(cx, cy, fogged);
        return ValueTask.CompletedTask;
    });
    return error is null ? Result.Success : Result.FromError(error);
}

public Task<Result> RevealCellsAsync(DndMapperGameState state, User caller, Guid mapId, IReadOnlyList<(int, int)> cells)
    => PaintFogAsync(state, caller, mapId, cells, fogged: false);

public Task<Result> HideCellsAsync(DndMapperGameState state, User caller, Guid mapId, IReadOnlyList<(int, int)> cells)
    => PaintFogAsync(state, caller, mapId, cells, fogged: true);

public async Task<Result> FillMapWithFogAsync(DndMapperGameState state, User caller, Guid mapId)
{
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(caller);
    if (!IsHost(state, caller))
        return Result.FromError("Only the host can change fog.");
    string? error = null;
    await state.ExecuteAsync(() =>
    {
        var map = state.Maps.FirstOrDefault(m => m.Id == mapId);
        if (map is null) { error = "Map not found."; return ValueTask.CompletedTask; }
        var bytes = (map.Grid.WidthCells * map.Grid.HeightCells + 7) / 8;
        map.FogMask = new byte[bytes];
        for (var i = 0; i < bytes; i++) map.FogMask[i] = 0xFF;
        // Trim trailing bits beyond Width*Height so IsFogged stays correct
        // on the unused tail of the last byte (it doesn't, since IsFogged
        // bounds-checks first, but keeping the mask exact is cheaper to
        // reason about during testing).
        var totalBits = map.Grid.WidthCells * map.Grid.HeightCells;
        var trailing = bytes * 8 - totalBits;
        if (trailing > 0)
            map.FogMask[bytes - 1] &= (byte)((1 << (8 - trailing)) - 1);
        return ValueTask.CompletedTask;
    });
    return error is null ? Result.Success : Result.FromError(error);
}

public async Task<Result> ClearAllFogAsync(DndMapperGameState state, User caller, Guid mapId)
{
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(caller);
    if (!IsHost(state, caller))
        return Result.FromError("Only the host can change fog.");
    string? error = null;
    await state.ExecuteAsync(() =>
    {
        var map = state.Maps.FirstOrDefault(m => m.Id == mapId);
        if (map is null) { error = "Map not found."; return ValueTask.CompletedTask; }
        map.FogMask = [];
        return ValueTask.CompletedTask;
    });
    return error is null ? Result.Success : Result.FromError(error);
}
```

Out-of-bounds cells in `PaintFogAsync` are silently dropped by `SetFogged` (matches the pattern image-transform verbs use for clamped values — see the existing `UpdateImageTransformAsync` for prior art).

### 3.3 `DndMapperLibraryService` persistence

Locate the per-map serialization snapshot inside `DndMapperLibraryService.cs`. Add `FogMask` to the DTO that mirrors `Map`. Two notes:

1. **Migration**: old saves don't have a `FogMask` field. Default-deserializing to `[]` (empty array, "no fog") is the correct behavior — no schema version bump needed.
2. **Wire size**: a fully-fogged 100×100 map adds 1,250 bytes per map to the snapshot. Acceptable.

Verify during implementation that the serializer (System.Text.Json) handles `byte[]` as base64 by default — it does, so a fully-fogged 100×100 map serializes to ~1,700 characters. Document this in a one-line comment so the next reviewer doesn't think the field is mis-typed.

---

## 4. Tests

### `FogMaskTests.cs` — pure helpers (no engine, no state)

1. `IsFogged_EmptyMask_ReturnsFalse` — fresh map, any `(cx, cy)` returns `false`.
2. `SetFogged_AllocatesOnFirstCall` — `FogMask.Length == 0` before, then `> 0` after a `SetFogged(0, 0, true)` call.
3. `SetFogged_OutOfBounds_NoOp` — call with `cx = -1` or `cx >= WidthCells`; `FogMask` stays empty.
4. `SetFogged_Roundtrip` — set `(3, 4) = true`, read it back, set it `false`, read it back.
5. `SetFogged_MultipleCells_NoCrosstalk` — set `(0, 0)` and `(99, 99)` on a 100×100 map; `(0, 1)`, `(1, 0)`, and `(98, 99)` all stay false.

### `FogOfWarVerbsTests.cs` — engine verbs

Use the existing `Moq.AutoMock` pattern visible in `MapVerbsTests.cs` as the template.

1. `PaintFog_Host_SetsCellsToTrue`.
2. `PaintFog_Host_ClearsCellsWhenFoggedFalse`.
3. `PaintFog_NonHost_ReturnsFailure_HostOnlyMessage`.
4. `PaintFog_UnknownMapId_ReturnsFailure_MapNotFound`.
5. `PaintFog_EmptyCellList_ReturnsSuccess_NoMutation`.
6. `PaintFog_OutOfBoundsCells_OnlyInBoundsApplied`.
7. `FillMapWithFog_AllocatesAndSetsAllBitsTrue`.
8. `FillMapWithFog_DoesNotSetBitsBeyondGridSize` — e.g. on a 3×3 grid the FogMask is 2 bytes (16 bits) but only the first 9 should read as fogged via `IsFogged`. (`SetFogged`'s bounds check guarantees this regardless; the test asserts behavior.)
9. `ClearAllFog_ResetsMaskToEmpty`.
10. `RevealCells_IsPaintFogFalse` and `HideCells_IsPaintFogTrue` — behavioral equivalence smoke tests.
11. `PaintFog_OnMapA_DoesNotAffectMapB`.

### `DndMapperLibraryService` save/load test

Add or extend the existing library-service test (find it under `host/KnockBox.DndMapperTests/Unit/`): paint a few cells, snapshot, hydrate a fresh state from the snapshot, assert `IsFogged` returns the same values.

---

## 5. Verification (engine-only, pre-UI)

`dotnet test host/KnockBox.DndMapperTests/KnockBox.DndMapperTests.csproj` passes.

No manual UI verification needed at this milestone — fog has no rendering yet. M03 wires the UI and M05 runs the full multi-browser matrix.
