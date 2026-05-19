# M05 — End-to-End Verification & Polish

> **Goal**: run a three-browser verification matrix (host + player + display) covering the joint behavior of fog, hidden tokens, hidden images, map switching, save / reload, and the missing-room UX. Fix any regressions surfaced, but ship no new features. Closes the Display + Fog body of work.
>
> **Dependencies**: M01–M04.
>
> **GDD references**: §17.5 (display verification matrix — extended here with fog cases).
>
> **Out of scope**: any new feature. M05 is verification + cleanup only.

---

## 1. Context

By M04, every code path is in place. M05 exists because the joint behavior of fog + hidden tokens + map switching + library save has more surface area than any single milestone can verify in isolation. A real session involves all of them at once.

The matrix below is the contract. Anything that fails is a bug to fix; if a bug surfaces that requires more than a one-line fix, file it under `Specs/Plans/Implementation/IssuesFound.md` following the same convention M01–M06 used.

---

## 2. Verification matrix

Setup: `dotnet run --project host/KnockBox/KnockBox.csproj`. Open three browser tabs against the same host:

- **Host tab**: create a new DnD Mapper lobby. Start the game.
- **Player tab**: join the lobby with a second user identity.
- **Display tab**: open `/room/dnd-mapper/{ObfuscatedRoomCode}/display`.

| # | Action (Host) | Expected Host | Expected Player | Expected Display |
|---|---------------|---------------|-----------------|------------------|
| 1 | Open Fog panel; Paint mode; brush 1; drag a 3×3 patch. | Translucent black square over the 9 cells. Cells under the patch still visible. | Solid black square over the 9 cells. | Same as Player. |
| 2 | Place an NPC token on the center of the patch. | Token visible through the translucent fog. | Token not in DOM (dev tools confirm). | Same as Player. |
| 3 | Switch to Erase mode; brush 1; click the center cell. | Center cell clears; token now sits on a revealed cell with 8 fog cells around it. | Token appears. | Same as Player. |
| 4 | Mark the token `Hidden = true`. | Token shows with ghost / eye-slash overlay (existing behavior). | Token disappears even though its cell is revealed. | Same as Player. |
| 5 | Mark the token `Hidden = false`. | Ghost overlay clears. | Token reappears. | Same as Player. |
| 6 | Upload a 4×4 image positioned over the fog patch (which is 3×3, so the image extends one cell past on each side). | Image visible. | Image visible (corners on cells `(x-1, y-1)`, `(x+4, y-1)`, etc. are revealed). | Same as Player. |
| 7 | Paint fog over all four corner cells of the image so its entire bounding box is fogged. | Translucent overlay covers the image area; image still editable. | Image disappears entirely. | Same as Player. |
| 8 | Erase one corner cell. | Same. | Image reappears. | Same as Player. |
| 9 | Click "Fill with fog" → confirm. | Entire active map translucent-black. | Entire map opaque-black. No tokens, no images in DOM (only the bare grid + markup if any). | Same as Player. |
| 10 | Click "Clear all fog" → confirm. | Fog gone. | Fog gone; previously-hidden tokens / images reappear. | Same as Player. |
| 11 | Paint a small fog patch. Create a second map; switch active map. | Map B has no fog; Map A still has its patch (verified by switching back). | Switches to Map B (no fog). | Switches to Map B. |
| 12 | Stay on Map B; refresh the Display tab. | (Host tab unaffected.) | (Player tab unaffected.) | Display reattaches and shows Map B's current state. |
| 13 | While Display tab is open, host clicks "End Session". | Returns to home. | Returns to home (or grace-period banner depending on existing behavior). | Display stops re-rendering. The observer attachment disposes cleanly with no exceptions in browser console or server log. |
| 14 | Navigate Display to a `/display` URL with a code that was never created. | n/a | n/a | Display shows static "Room not found." with the invalid code in `<code>`. No auto-retry. |
| 15 | Recreate the lobby. From the host, click "Open display view" (M01 button). | New tab opens at the canonical display URL. Toast says "Display link copied to clipboard." Pasting into a fourth tab opens the same URL. | n/a | New display tab attaches and shows the active map. |
| 16 | Host save library → close session → reload host into a fresh session and load the saved library. | Maps, fog masks, tokens all restored. | n/a | n/a |
| 17 | Performance smoke: create a 60×60 map, fill with fog, drag-erase a long stroke across it. | Stroke renders without visible jank; engine round-trip per flush stays under ~50 ms (DevTools Network panel or server-side log). | Player tab reflects each flush within one SignalR round-trip. | Same as Player. |

---

## 3. Bug-handling protocol

For each row that fails, classify:

- **Trivial** (one-line fix, typo, wrong CSS class) — fix in place, rerun the matrix.
- **Non-trivial** — write it up at the bottom of `host/KnockBox.DndMapper/Specs/Plans/Implementation/IssuesFound.md` under a new "M05 (FoW + Display)" heading. Each entry: matrix row number, observed vs expected, suspected file, severity. Then either fix it in this milestone if low-risk, or defer to a follow-on PR.

A non-exhaustive list of edge cases likely to surface:

- Host fog overlay rendering rendered *over* the token layer instead of under it (z-order bug from M03).
- Player canvas not unsubscribing / re-subscribing to `StateChangedEventManager` on map switch — stale fog from the previous map.
- `DisplayProjection.FoggedCells` not being recomputed when `FogMask` mutates but `ActiveMapId` doesn't change (state-change event still fires, but the projection's identity comparison might skip rebuild).
- Library service deserializing `FogMask` as `null` instead of `[]` on old saves — `Map.IsFogged` is null-safe via `FogMask.Length == 0`, but the property setter must coalesce.

---

## 4. Polish opportunities (only if time allows in M05)

Defer unless a matrix row surfaces them:

- **Switch fog rendering from per-cell `<rect>` to a single `<path>`** when the visible fogged-cell count exceeds ~1000. Improves Blazor re-render time on large maps. Measure before optimizing.
- **Toast confirmation** when the host's Fill / Clear actions complete (already implicit from the visible state change; toast may be redundant).
- **Keyboard shortcut** for fog mode toggle (e.g. `F` to cycle Off → Paint → Erase). Nice-to-have, low priority.

---

## 5. Definition of done

- All 17 matrix rows pass.
- `dotnet test host/KnockBox.Host.slnx` is green.
- `dotnet test sdk/KnockBox.Sdk.slnx` is green (no regressions in `GameRoomObserverTests` or `LobbyServiceTests`).
- `dotnet build host/KnockBox.Host.slnx` succeeds with no new warnings on the touched projects.
- `IssuesFound.md` either has no new "M05 (FoW + Display)" entries, or every entry is marked Closed with a referenced commit.
