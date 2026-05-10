# M05 — UI Panels (Host Authoring + Sheets + Dice)

> **Goal**: ship the polished GM and player panels that make the canvas usable without debug verbs. Adds the host's map switcher, image inspector + upload control, token panel, permissions panel, plus the player-shared character sheet panel, dice roller modal, and roll log panel. After M05 the game is fully usable: no debug commands, no curl invocations.
>
> **Dependencies**: M01 (state + engine verbs — every panel calls one or more verbs from M01), M02 (HTTP dispatcher — image upload control hits the M02 endpoint), M03 (image management endpoint + verbs), M04 (canvas + token drag — panels compose around the canvas in `DndMapperPlayingPhase`).
>
> **GDD references**: §4.3 (map switcher UI), §5 (image upload UX, transform handles, layers panel), §7.4 (host tokens panel), §8 (character sheet UI rules), §9.4 (dice modal UX), §11 (permissions UI), §13 (component file list).
>
> **Out of scope** (do NOT implement here): lobby pre-session authoring (M06 reuses these components in the lobby phase). End-session button (M06). Display view (v1.x). Initiative banner / panel (v1.x). Audio / video chat / soundboard (v1.x).

---

## 1. Context

After M04 the canvas renders maps and tokens, players can drag, hidden tokens hide correctly, and remote moves animate. But the only way to *create* anything — maps, images, tokens — is via debug verbs or M03's `curl` flow. M05 closes the loop: every engine verb gets a UI surface that a non-developer host or player can use.

The panels split into two visibility tiers:
- **Host-only**: `HostMapSwitcher`, `HostImageInspector`, `ImageUploadButton`, `HostTokenPanel`, `PermissionsPanel`.
- **Shared (host + players)**: `CharacterSheetPanel`, `DiceRollerModal`, `RollLogPanel`. Each enforces its own visibility rules per §8.3 and §9.3.

M05 is the largest UI milestone. To keep it auditable, components are independent — most can be built and reviewed in isolation. The integration step (composing them into `DndMapperPlayingPhase.razor`) is the only place where they touch.

The visibility logic for sheets and rolls is non-trivial and should be **extracted into pure C# helpers** so it gets unit-test coverage — this is the only place in M05 where unit tests catch regressions effectively.

---

## 2. Files to create / modify

### New components (all under `host/KnockBox.DndMapper/Pages/Components/`)

```
HostMapSwitcher.razor (+.razor.cs, +.razor.css)
HostImageInspector.razor (+.razor.cs, +.razor.css)
ImageUploadButton.razor (+.razor.cs, +.razor.css)
HostTokenPanel.razor (+.razor.cs, +.razor.css)
PermissionsPanel.razor (+.razor.cs, +.razor.css)
CharacterSheetPanel.razor (+.razor.cs, +.razor.css)
DiceRollerModal.razor (+.razor.cs, +.razor.css)
RollLogPanel.razor (+.razor.cs, +.razor.css)
ConfirmModal.razor (+.razor.cs, +.razor.css)        ; small reusable confirm helper for destructive ops + schema change
```

### New pure-C# helpers (under `host/KnockBox.DndMapper/Services/Logic/Visibility/`)

```
RollLogVisibilityFilter.cs
SheetVisibilityHelper.cs
TokenColorContrast.cs    ; (optional: if M04 didn't already extract)
```

### New test classes (under `host/KnockBox.DndMapperTests/Unit/Logic/Visibility/`)

```
RollLogVisibilityFilterTests.cs
SheetVisibilityHelperTests.cs
```

### Files to modify

- `host/KnockBox.DndMapper/Pages/DndMapperPlayingPhase.razor` — extend the layout from M04's minimal canvas-only version to the full panel composition (see §3.10).
- `host/KnockBox.DndMapper/Pages/DndMapperPlayingPhase.razor.css` — full layout grid (CSS Grid or Flexbox).
- (Optional) `host/KnockBox.DndMapper/wwwroot/css/...` — any shared styles.

### Files NOT touched in M05

- `Services/State/Games/*` — no state shape changes.
- `Services/Logic/Games/DndMapperGameEngine.cs` — no new verbs.
- `Services/Logic/Games/Http/*` — no HTTP changes; the upload button calls the existing M03 endpoint.
- `Pages/DndMapperLobby.razor` — M06 adds lobby authoring (which reuses M05 components). M05 leaves the lobby alone.
- `plugin.json`, `KnockBox.DndMapper.csproj` — unchanged.

---

## 3. Detailed work breakdown

### 3.1 `HostMapSwitcher.razor`

A vertical sidebar list of maps with thumbnails. Per §4.3:

- Each row: thumbnail (first image in `Map.Images` if any, else a generic icon), `Map.Name`, drag handle.
- Single-click row: `engine.SetActiveMapAsync(state, host, map.Id)`.
- Drag handle reorders rows; commit calls `engine.ReorderMapsAsync(state, host, orderedIds)`.
- `+` button at the top: prompts for a name, calls `engine.CreateMapAsync(state, host, name)`.
- Right-click / 3-dot menu per row: rename / duplicate / delete (delete prompts via `ConfirmModal`).
- Active map row visually distinguished (e.g. left border accent).

Drag-reorder reuses HTML5 drag-and-drop APIs (no SVG, no JS module). Capture `dragstart` → record source index, `dragover` → set drop target, `drop` → emit a new ordered list and call `ReorderMapsAsync`.

```csharp
[Parameter] public DndMapperGameState State { get; set; } = default!;
[Inject] DndMapperGameEngine GameEngine { get; set; } = default!;
[CascadingParameter] public User CurrentUser { get; set; } = default!;

// Rendered only when IsHost == true; the parent gates this.
```

### 3.2 `HostImageInspector.razor`

When the host clicks an image on the canvas, the inspector shows:
- Sliders / numeric fields for X, Y, Width, Height (in cells), Rotation (degrees), Opacity (0–1).
- Layer up / down buttons.
- Lock toggle.
- Delete button (destructive — prompts via `ConfirmModal`).

Two interaction patterns:
1. **Canvas-driven**: drag the image's corner handle on the canvas → drag-end fires `engine.UpdateImageTransformAsync`.
2. **Inspector-driven**: edit numeric fields → on commit (focus loss / Enter), call `engine.UpdateImageTransformAsync`.

Both go through the same engine verb. Drag handles on the canvas need the host-only inspector's interaction layer:

> **Canvas-side handles**: `MapCanvas.razor` (M04) renders images but doesn't draw transform handles. M05 adds a `<g class="image-handles">` overlay that renders only when `IsHost && SelectedImage is not null`. Corner handles are small squares the host can drag; rotate handle is a circle above the image. Each commits via `UpdateImageTransformAsync` on mouse-up.

`HostImageInspector.razor` is the side-panel UI that mirrors the canvas handles for keyboard / numeric input. Selection state (`Guid? SelectedImageId`) lives on `DndMapperPlayingPhase` and cascades down.

### 3.3 `ImageUploadButton.razor`

Host-only. Single Blazor `<InputFile>`:

```razor
<InputFile OnChange="OnFileSelected" accept="image/png,image/jpeg,image/webp" />
```

`OnFileSelected` flow — **calls `engine.SaveImageAsync` directly** (no HTTP, no cookie). The auth model decision in M02 / M03 routes upload through the in-process engine method; the HTTP dispatcher is GET-only:

1. Get the file from the event: `var file = e.File;` (only one file allowed; `accept` restricts via the browser).
2. Open as a stream: `using var stream = file.OpenReadStream(maxAllowedSize: 5 * 1024 * 1024);`. Browser-side cap matches the engine's per-file cap; the engine validates again as defense-in-depth.
3. Resolve the active map id: `Guid mapId = State.ActiveMapId ?? throw new InvalidOperationException("No active map.");` — disable the button when `State.ActiveMapId is null` so this path isn't hit.
4. Call `var result = await GameEngine.SaveImageAsync(State, UserService.CurrentUser, mapId, stream, file.Size);`.
5. On `result.TryGetSuccess(out _)`: nothing further to do — `SaveImageAsync` calls `AddImageAsync` internally, which broadcasts via `state.Execute` → the canvas re-renders.
6. On `result.TryGetFailure(out var err)`: surface a toast with `err.PublicMessage` (covers oversize, bad MIME, over-room-cap, unknown map, host-only).

Code-behind injects `DndMapperGameEngine`, `IUserService`, and the toast helper (or the in-component minimal toast described in §3.11).

> **Why no HTTP**: M02 made the dispatcher anonymous-by-design (no user-circuit cookie exists in v1); routing the upload through HTTP would require either a new platform cookie scheme or a fragile cookie-tunneling hack. The in-process engine call has the user from `IUserService.CurrentUser` (circuit-bound, trustworthy) and skips the boundary entirely. See M03 §3.4.

### 3.4 `HostTokenPanel.razor`

Host-only list of NPC + host-extra tokens (filtered by `Token.Type != PlayerToken`). Each row:
- Name (editable inline → `engine.UpdateTokenAsync`).
- Color swatch + picker.
- Icon kind toggle (Initial / Solid).
- Sheet attach / detach button (opens a sheet picker).
- "Represents player" dropdown (for host-extra tokens) — selects from registered players.
- Hidden flag toggle → `engine.SetTokenHiddenAsync`.
- Delete button → `engine.RemoveTokenAsync` (destructive; prompts via `ConfirmModal`).

Top of panel: `+ Add NPC` and `+ Add Host Extra` buttons. Each spawns at the active map's center via `engine.SpawnNpcTokenAsync` / `engine.SpawnHostExtraTokenAsync`.

### 3.5 `PermissionsPanel.razor`

Host-only. Mirrors `state.Settings`:
- `TokenMovement`: 3-button radio (`OwnerOrHost` / `Anyone` / `HostOnly`).
- `SheetEditByOthers`: 3-button radio.
- `RollsVisibleToPlayers`: toggle.
- `PlayersCanCreateNPCs`: toggle.

On any change → call `engine.UpdateSettingsAsync(state, host, newSettings)`. Editable mid-session per §11.

### 3.6 `CharacterSheetPanel.razor` (player + host)

Visibility rules (§8.3):
- All participants see character names + attribute values.
- Notes + HP visible only to the sheet owner and the host.

Editing rules (per `state.Settings.SheetEditByOthers`):
- `OwnersOnly` / `OwnersAndHost`: caller must own the sheet, OR be host (host always exempt).
- `Anyone`: any participant may edit.

UI:
- Tabs / dropdown to switch between sheets the user is allowed to see (own + others). The current player's own sheet is the default tab.
- Attribute rows: rendered per `state.AttributeSchema.Rows`. `Score` rows show the score + auto-derived modifier; `Modifier` rows show just the modifier; `Text` rows show a textbox.
- HP row (visible to owner + host only): two numeric fields with +/− steppers per O-7, plus direct numeric entry. Both `Hp` and `MaxHp` are nullable — if null, render an "Add HP tracking" button that initializes both to 0; if non-null, render the steppers.
- Notes: a `<textarea>` (visible to owner + host only).
- Character name: editable for owner + host.

Each commit calls `engine.UpdateSheetAttributeAsync` or `engine.UpdateSheetFreeFieldsAsync`. Use debounce (e.g. 500 ms after focus loss) for textareas to avoid spamming verb calls during typing.

> **`SheetVisibilityHelper`** (pure C# helper, in `Services/Logic/Visibility/`):
>
> ```csharp
> public static class SheetVisibilityHelper
> {
>     public static bool CanSeeNotesAndHp(CharacterSheet sheet, string viewerUserId, bool viewerIsHost)
>         => viewerIsHost || sheet.OwnerUserId == viewerUserId;
>
>     public static bool CanEdit(CharacterSheet sheet, string viewerUserId, bool viewerIsHost, SheetEditPolicy policy)
>     {
>         if (viewerIsHost) return true;
>         if (sheet.OwnerUserId == viewerUserId) return true;
>         return policy == SheetEditPolicy.Anyone;
>     }
> }
> ```
>
> Pure functions, easily unit-testable.

### 3.6.1 Schema preset selector — owned by M06

GDD §8.1 says re-picking the schema mid-session "opens a host-only confirmation modal" describing the cascade. **The schema selector + cascade-warning modal live in M06**, not M05. M06 §3.2 / §3.3 own `SchemaPresetSelector.razor` and `SchemaCascadeWarningModal.razor`; both are reusable in either phase (lobby or playing). Earlier drafts of M05 implied the schema selector belonged inside `CharacterSheetPanel` — that's wrong. The selector lives in:

- **Lobby phase**: rendered in `DndMapperLobby.razor` (M06 §3.1) above the player roster — pre-game configuration.
- **Playing phase**: rendered as a host-only floating action button or top-of-`PermissionsPanel` row, opening the same `SchemaCascadeWarningModal` before commit. Wire-up happens in M06's pass over `DndMapperPlayingPhase.razor`.

M05's `CharacterSheetPanel` does NOT include the schema selector. Cross-reference M06 to avoid duplicate work.

### 3.7 `DiceRollerModal.razor`

Per §9.4. A modal that opens via a floating "Roll" button:

- Quick-roll buttons: `d20`, `2d6`, `Initiative` (a labeled `1d20 + DEX` for the current user's sheet).
- Custom dice composer: dropdowns for count + sides (constraint: sum of counts ≤ 20).
- Attribute dropdown: lists attributes from `state.AttributeSchema.Rows` whose type is `Score` or `Modifier` (skip `Text`). Source sheet is the current user's own sheet (or, if host, a sheet picker).
- Mode toggle: Normal / Advantage / Disadvantage. Adv / Dis disabled unless the current dice composition is exactly `1d20`.
- Label text field.
- Submit button → `engine.RollAsync(state, currentUser, request)`. Closes modal on success; shows the error toast on failure.

### 3.8 `RollLogPanel.razor`

Sticky panel showing the last 20 rolls *visible to the current viewer*. Filtering rules (§9.3):
- Host: sees every roll, always.
- Player: sees own rolls + (if `RollsVisibleToPlayers = true`) every other player's rolls.

Each entry renders: roller name (looked up from `state.Players`), label, dice expression (`2d6+3` style), individual die results (with discarded ones struck-through for adv/dis), and total.

> **`RollLogVisibilityFilter`** (pure C# helper):
>
> ```csharp
> public static class RollLogVisibilityFilter
> {
>     public static IEnumerable<RollResult> VisibleTo(
>         IEnumerable<RollResult> log, string viewerUserId, bool viewerIsHost, bool rollsVisibleToPlayers)
>     {
>         if (viewerIsHost) return log;
>         if (rollsVisibleToPlayers) return log;
>         return log.Where(r => r.RollerUserId == viewerUserId);
>     }
> }
> ```
>
> The panel shows the last 20 entries from this filtered list (newest at the bottom).

### 3.9 `ConfirmModal.razor`

Small reusable confirmation modal for destructive ops:
- `[Parameter] string Title { get; set; }`
- `[Parameter] string Message { get; set; }`
- `[Parameter] string ConfirmText { get; set; } = "Confirm"`
- `[Parameter] EventCallback OnConfirm { get; set; }`
- `[Parameter] EventCallback OnCancel { get; set; }`

Used by `HostMapSwitcher` (delete map), `HostTokenPanel` (delete token), `HostImageInspector` (delete image), and (in M06) the schema-change cascade warning.

### 3.10 `DndMapperPlayingPhase.razor` extension

From M04's minimal canvas-only version to the full layout:

```razor
@inherits DisposableComponent
@implements IAsyncDisposable
@namespace KnockBox.DndMapper.Pages

<div class="dnd-mapper-playing-grid">

    @if (IsHost)
    {
        <aside class="left-rail">
            <HostMapSwitcher State="GameState" />
            <HostTokenPanel State="GameState" />
            <ImageUploadButton State="GameState" RoomCode="@RoomCode" ActiveMapId="@GameState.ActiveMapId" />
        </aside>
    }

    <main class="canvas-area">
        <MapCanvas State="GameState" Map="ActiveMap" IsHost="IsHost" CurrentUserId="CurrentUserId" />
        @if (IsHost && SelectedImageId is Guid id)
        {
            <HostImageInspector State="GameState" MapId="@GameState.ActiveMapId!.Value" ImageId="@id" />
        }
    </main>

    <aside class="right-rail">
        <CharacterSheetPanel State="GameState" CurrentUserId="@CurrentUserId" IsHost="IsHost" />
        <RollLogPanel State="GameState" CurrentUserId="@CurrentUserId" IsHost="IsHost" />
    </aside>

    <div class="floating-actions">
        <button @onclick="ToggleDiceModal">🎲 Roll</button>
        @if (IsHost)
        {
            <button @onclick="TogglePermissionsPanel">⚙ Permissions</button>
        }
    </div>

    @if (_diceOpen) { <DiceRollerModal State="GameState" CurrentUserId="@CurrentUserId" OnClose="ToggleDiceModal" /> }
    @if (IsHost && _permsOpen) { <PermissionsPanel State="GameState" OnClose="TogglePermissionsPanel" /> }
</div>
```

Layout via CSS Grid (3 columns: left rail, main, right rail) with the floating actions absolute-positioned bottom-right.

> **Don't use emojis in the dev UI** unless asked — replace `🎲` and `⚙` with text labels `"Roll"` / `"Permissions"` or icon SVGs. The placeholder above is illustrative only.

### 3.11 Toasts / error surfacing

`engine.UpdateSettingsAsync` and friends return `Result`. On failure, surface the `ResultError.PublicMessage` as a toast. M05 can either:
- Add a small `Toast.razor` shared component.
- Or piggyback on `SpardleToast.razor`'s pattern by adapting it.

For brevity, M05 ships a minimal `<div class="toast-stack">` rendering recent transient messages with auto-dismiss after 4 seconds. Polish in M06 if needed.

---

## 4. Acceptance criteria

- [ ] `dotnet build host/KnockBox.Host.slnx` succeeds.
- [ ] `dotnet test host/KnockBox.DndMapperTests/KnockBox.DndMapperTests.csproj` is green, including the new visibility-helper tests.
- [ ] M01–M04 tests still green.
- [ ] In a two-browser session (host + player), the host can:
  - Create / rename / duplicate / reorder / delete maps via the switcher.
  - Set the active map; player view follows.
  - Upload an image; both clients see it on the active map.
  - Drag the image's transform handles; both clients see the new position/rotation/opacity.
  - Adjust layer order via the inspector.
  - Delete an image; the file is removed from disk (verify via `ls`).
  - Add NPC + Host Extra tokens; rename, recolor, set hidden, delete.
  - Edit any character sheet (host always exempt from `SheetEditByOthers`).
  - Toggle every entry in `PermissionsPanel`; effect is immediate.
- [ ] In the same session, the player can:
  - Edit own character sheet (under `OwnersOnly` / `OwnersAndHost`).
  - Cannot edit others' sheets unless `SheetEditByOthers = Anyone`.
  - Roll dice via `DiceRollerModal`; result appears in own log.
  - With `RollsVisibleToPlayers = true`, sees other players' rolls; with `false`, sees only own.
  - Cannot see Notes / HP on other players' sheets.

---

## 5. Manual verification

The full GDD §17 verification matrix is M06's responsibility; M05's manual verification is a subset:

1. Two browsers; create + join room.
2. Host creates two maps via `HostMapSwitcher`. Switches between them. Both clients see the active map flip.
3. Host uploads a 1 MB PNG. Confirms the upload completes (no 4xx) and both clients see the image render.
4. Host drags image transform handles to scale + rotate. Releases; both clients see the new transform within ~100–200 ms.
5. Host moves an image up a layer; both clients see the layer reorder.
6. Host deletes a map (confirms via `ConfirmModal`). All images on it are gone from disk.
7. (Schema-change scenario deferred to M06's verification — the `SchemaPresetSelector` and `SchemaCascadeWarningModal` land there. M05 just honors whatever `state.AttributeSchema` currently is.)
8. Player rolls `1d20 + STR` — appears in roll log.
9. Host sets `RollsVisibleToPlayers = false`. Player rolls again. Player sees own roll; host sees both; the *other* player browser (if a third is present) sees only their own.
10. Host sets `TokenMovement = HostOnly`. Player tries to drag own token — blocked.

---

## 6. Inline unit test plan

### 6.1 `RollLogVisibilityFilterTests`

- `VisibleTo_HostSeesAllRolls`.
- `VisibleTo_RollsVisibleToPlayersTrue_AllPlayersSeeAll`.
- `VisibleTo_RollsVisibleToPlayersFalse_PlayerSeesOnlyOwn`.
- `VisibleTo_EmptyLog_ReturnsEmpty`.
- `VisibleTo_LogContainsOtherPlayersRolls_FilteredCorrectly`.

### 6.2 `SheetVisibilityHelperTests`

- `CanSeeNotesAndHp_OwnerTrue`.
- `CanSeeNotesAndHp_HostTrue`.
- `CanSeeNotesAndHp_OtherPlayerFalse`.
- `CanEdit_OwnersOnly_OwnerTrue`.
- `CanEdit_OwnersOnly_OtherPlayerFalse`.
- `CanEdit_OwnersOnly_HostTrue`.
- `CanEdit_OwnersAndHost_BehavesLikeOwnersOnly` — documented identical behavior; assert.
- `CanEdit_Anyone_AllParticipantsTrue`.

### 6.3 (Optional) Component tests via bUnit

If the project doesn't yet use bUnit, M05 can skip component tests in favor of manual verification. If bUnit is added, candidate tests:

- `HostMapSwitcher_ClickRow_CallsSetActiveMap`.
- `RollLogPanel_FiltersBasedOnVisibility` — covered by `RollLogVisibilityFilterTests` already; component test would be redundant.
- `CharacterSheetPanel_HpHiddenForNonOwner`.

Given the existing codebase doesn't ship bUnit, M05 stays with helper-level unit tests + manual verification. Document the choice in PR.

---

## 7. Implementation choices to flag during PR

- **Image upload calls `engine.SaveImageAsync` directly** (no HTTP boundary) per the M02 / M03 auth-model decision. `ImageUploadButton.razor` is just a thin Blazor wrapper around `<InputFile>` + the engine call. No `HttpClient`, no cookie complexity.
- **Toast component**: M05 ships a minimal toast; M06 may polish. If `SpardleToast.razor` is reusable, lift it into a shared SDK component instead of forking. Defer to author's preference.
- **Schema editor location**: NOT in `CharacterSheetPanel`. Owned by M06 (`SchemaPresetSelector` + `SchemaCascadeWarningModal`) — see §3.6.1. M05's only schema-related responsibility is honoring the current `state.AttributeSchema` when rendering attribute rows.
- **Layout direction**: M05 picks 3-column CSS Grid with fixed-width side rails. If smaller screens become a problem, collapse to a single column with collapsible rails. Out of scope for v1 (desktop-first per §15).
- **Bulk operations** — duplicating maps, bulk-spawning tokens — left out of M05 unless trivial. Each affordance maps to one engine verb call.
- **Confirmation modal styling**: a non-blocking dim-overlay pattern is sufficient. Avoid full-page modals that disrupt the canvas.
