# M06 — Lobby Polish, End-Session, Verification Matrix

> **Goal**: close out v1 by adding lobby pre-session authoring (host can build maps + configure settings before clicking Start), wiring the End-Session button, and running the full GDD §17 v1 verification matrix as the milestone's primary deliverable.
>
> **Dependencies**: all of M01–M05 must be merged. M06 is purely composition + verification, not new mechanics.
>
> **GDD references**: §3 (lifecycle — lobby authoring allowed per O-2), §8.1 (mid-session schema-change cascade — host-only modal confirmation), §17 (verification — entire section).
>
> **Out of scope** (do NOT implement here): combat / initiative tracker (v1.x). Display view + observer-attach (v1.x). Anything in §15. Cross-session save/reload. Bulk-import tools.

---

## 1. Context

After M01–M05 the game is fully functional during the *Playing* phase: host creates and configures everything mid-session via the panels in M05. But the lobby phase is still bare — players join, host clicks Start, and the canvas appears with whatever defaults the engine seeded. Per GDD §3 and O-2, the host should be able to **pre-author maps and adjust settings during the lobby phase** so the game starts with content already in place.

M06 also wires the explicit "End Session" affordance (currently exposed only via direct verb calls) and runs the full §17 verification matrix. This is the gate for shipping v1: every verification line item passes, no items in §15 are accidentally implemented, no v1.x scaffolding (combat fields, display view) leaked into the v1 build.

---

## 2. Files to create / modify

### New files

```
host/KnockBox.DndMapper/Pages/Components/EndSessionButton.razor (+.razor.cs, +.razor.css)
host/KnockBox.DndMapper/Pages/Components/SchemaPresetSelector.razor (+.razor.cs, +.razor.css)
host/KnockBox.DndMapper/Pages/Components/SchemaCascadeWarningModal.razor   ; uses ConfirmModal pattern from M05
```

### Files to modify

- `host/KnockBox.DndMapper/Pages/DndMapperLobby.razor` — add a host-only authoring section: `HostMapSwitcher` (M05) + `ImageUploadButton` (M05) + `PermissionsPanel` (M05) + `SchemaPresetSelector` (new). Player rows + Start button stay as-is.
- `host/KnockBox.DndMapper/Pages/DndMapperLobby.razor.cs` — gate the new authoring panels on `IsHost()`.
- `host/KnockBox.DndMapper/Pages/DndMapperLobby.razor.css` — layout for host authoring panels.
- `host/KnockBox.DndMapper/Pages/DndMapperPlayingPhase.razor` — add `EndSessionButton` to the floating actions row (host-only).

### Files NOT touched in M06

- Engine, state, HTTP handler — no changes.
- `plugin.json`, `KnockBox.DndMapper.csproj` — no changes.
- Any v1.x deferred files — they shouldn't exist yet.

---

## 3. Detailed work breakdown

### 3.1 Lobby authoring panel composition

In `DndMapperLobby.razor`, add a host-only authoring section. Reuse M05 components verbatim — they already accept `state` and operate via the same engine verbs that work in either phase (the verbs don't gate on `state.Phase` because §3 / O-2 explicitly allows lobby authoring).

```razor
@if (IsHost())
{
    <section class="lobby-authoring">
        <h3>Pre-game authoring (optional)</h3>

        <SchemaPresetSelector State="GameState" />

        <div class="authoring-grid">
            <HostMapSwitcher State="GameState" />
            <ImageUploadButton State="GameState" RoomCode="@RoomCode" ActiveMapId="@GameState.ActiveMapId" />
        </div>

        <PermissionsPanel State="GameState" Embedded="true" />
    </section>
}
```

The existing player roster + "Start" button stay unchanged below. When the host clicks Start, M01's `StartAsyncCore` flips phase to `Playing`, and any pre-authored maps + active map id carry forward. If the host left `ActiveMapId` unset, `StartAsyncCore` picks the first map by `ListOrder` (per M01 §3.5).

### 3.2 `SchemaPresetSelector.razor`

Host-only. A simple radio / dropdown of `AttributePreset` values. On change:

1. If `state.Phase == Lobby`: call `engine.ChangeSchemaAsync(state, host, AttributeSchema.FromPreset(picked))` immediately — no modal needed (no sheets exist yet to cascade).
2. If `state.Phase == Playing`: open `SchemaCascadeWarningModal` listing exactly what will happen — "Existing values keyed by name + matching type are preserved; non-matching attributes are reset to their default; attributes absent from the new schema are removed from all sheets." Then on confirm, call `engine.ChangeSchemaAsync`.

`AttributePreset.Custom` is host-only and opens a custom-row editor (rows: name + type + default). M06 ships a minimal editor: list of rows with add / remove / edit, save commits the schema as a `Custom` schema. Polish optional.

### 3.3 `SchemaCascadeWarningModal.razor`

Wraps `ConfirmModal` from M05 with a fixed message:

> "Changing the attribute schema mid-session will rebuild every existing character sheet. Attributes whose name + type match the new schema keep their value. Attributes that don't match are reset to defaults. Attributes not in the new schema are removed. This cannot be undone."

Confirm → invoke the `ChangeSchemaAsync` callback. Cancel → close.

### 3.4 `EndSessionButton.razor`

Host-only. Renders as a button in the playing-phase floating actions:

```razor
<button class="danger" @onclick="ConfirmEnd">End Session</button>

@if (_open)
{
    <ConfirmModal Title="End Session?"
                  Message="Ends the session for everyone, deletes uploaded image files, and returns all clients to the home page. Cannot be undone."
                  ConfirmText="End Session"
                  OnConfirm="DoEnd"
                  OnCancel="@(() => _open = false)" />
}

@code {
    [Parameter] public DndMapperGameState State { get; set; } = default!;
    [Inject] DndMapperGameEngine GameEngine { get; set; } = default!;
    [Inject] INavigationService Navigation { get; set; } = default!;
    [CascadingParameter] public User CurrentUser { get; set; } = default!;

    private bool _open;

    private void ConfirmEnd() => _open = true;

    private async Task DoEnd()
    {
        var result = await GameEngine.EndSessionAsync(State, CurrentUser);
        if (!result.TryGetSuccess(out _))
        {
            // toast the error and stay
            _open = false;
            return;
        }

        // The platform's session-disposal flow + LobbyPageBase will redirect
        // every client to home automatically. Host doesn't need to nav manually.
    }
}
```

`engine.EndSessionAsync` calls `state.Dispose()` (per M01 / M03), which triggers the storage cleanup hook from M03 + the platform's per-circuit redirect.

### 3.5 Player auto-spawn on mid-session join (verification only)

GDD §4.2: "Token and sheet records are created for all lobby players when the session starts; players joining mid-session have their token spawned immediately on the active map."

This is owned by **M01** — the `PlayerRegistered` event was added to `AbstractGameState` in M01 §3.7 (platform addition), and `DndMapperGameEngine.HandlePlayerJoined` (M01 §3.5) subscribes and auto-spawns on the active map when `Phase == Playing`. M06 has **no implementation work here** — only verification:

- Walk through the §5.3 two-browser scenario (host + player A starts session). Open a third browser, join as player B mid-session. Confirm:
  - Player B sees the active map with their own `PlayerToken` placed at the default-spawn position.
  - The host browser also sees player B's token appear within typical SignalR latency.
  - A `CharacterSheet` for player B exists in `state.Sheets`, seeded from the current `AttributeSchema`.

If the scenario fails, the bug lives in M01 (the `PlayerRegistered` event or the engine's join handler). Fix in M01 — do not patch in M06.

### 3.6 Host disconnect verification (no code change required)

GDD §2.3 says: "If the host does not return within grace, the engine runs `EndSessionAsync` and everyone returns home." The platform's per-circuit grace logic disposes `GameSessionState` when the user's grace expires, which (via `ISessionServiceProvider`) eventually disposes the underlying `DndMapperGameState`. Disposal triggers M03's storage cleanup. **No new code needed in M06** — this is pure manual verification. Run the scenario:

1. Host opens room, uploads an image, starts the game.
2. Host closes the browser without ending the session.
3. Wait 1 minute (the grace window).
4. Player browser sees the redirect home (via existing platform behaviour).
5. `ls host/KnockBox/data/plugins/dnd-mapper/{sessionId}/images/` returns empty (cleanup ran).

If step 4 or 5 fails, the issue lives in M01 / M03; document and fix there rather than in M06.

---

## 4. Acceptance criteria

- [ ] `dotnet build host/KnockBox.Host.slnx` succeeds.
- [ ] All unit tests across `sdk/KnockBox.Sdk.slnx` AND `host/KnockBox.Host.slnx` are green.
- [ ] Host can pre-author maps + upload images + adjust settings + change schema during the lobby phase. Clicking Start carries the configuration into the playing phase without data loss.
- [ ] Host can click "End Session" in the playing phase; modal confirms; on confirm, all clients return home.
- [ ] After end-session, the per-room storage directory is empty (no orphan images).
- [ ] **Full GDD §17 v1 verification matrix passes** — see §5 below for the line-by-line list with pass/fail tracking.
- [ ] No v1.x leakage — verify NONE of the following exist in the codebase: `ActiveCombat`, `CombatState`, `CombatantEntry`, `InitiativeBanner`, `HostInitiativePanel`, `DndMapperDisplay.razor`, `/display` route, observer-attach extension, hex-grid types, fog-of-war types, measurement-tool types, save-format types. (Run a `grep` for each before sign-off.)
- [ ] Architecture invariant: `KnockBox.csproj` has zero `using KnockBox.DndMapper.*`; the `<ProjectReference>` retains `ReferenceOutputAssembly="false" Private="false"`; `IGameEngineHttpHandler` lives in `KnockBox.Core`; `Program.cs` has no plugin-specific names.

---

## 5. Verification matrix (§17 line-by-line)

This matrix is the **primary deliverable** of M06. Each line is tracked as a checkbox; sign-off requires all checked. The implementer runs each scenario in a clean local env (delete `host/KnockBox/data/` before starting if needed) and pastes evidence (screenshots, log excerpts) into the PR.

### 5.1 Build and stage

- [ ] `dotnet build host/KnockBox.Host.slnx` (Release configuration) succeeds with zero warnings introduced by DnD Mapper milestones.
- [ ] `KnockBox.DndMapper.dll`, `plugin.json`, `KnockBox.DndMapper.staticwebassets.endpoints.json`, `KnockBox.DndMapper.bundle.scp.css`, and `wwwroot/` (incl. `js/dndMapperTokenDrag.js`) are present under `host/KnockBox/bin/Release/net10.0/games/KnockBox.DndMapper/`.
- [ ] `dotnet publish host/KnockBox/KnockBox.csproj -c Release` stages the same artifacts under `publish/games/KnockBox.DndMapper/`.

### 5.2 Unit tests

- [ ] `dotnet test sdk/KnockBox.Sdk.slnx` is green (catches M02 dispatcher + lobby-URI lookup regressions).
- [ ] `dotnet test host/KnockBox.Host.slnx` is green (catches engine-verb + image-handler + visibility-helper regressions).
- [ ] Coverage check: every verb listed in GDD §12 (excluding the v1.x combat verbs) has at least three test methods (happy / permission / invalid). Use a manual checklist.

### 5.3 Two-browser end-to-end (per GDD §17.3)

Run with two browsers (host + one player). Both authenticate; create + join the same room.

- [ ] Host uploads an image; both clients see it on the active map within ~1 second.
- [ ] Host transforms the image (drag corner, rotate, opacity); both clients see the new transform on commit.
- [ ] Host creates a second map; switcher reflects the new map; clicking it makes it active for both clients.
- [ ] Player drags own token; host sees the move within typical SignalR latency (~200 ms).
- [ ] Player tries to drag the host's NPC token under default permissions — blocked client-side AND server-side (engine returns failure even if the player bypasses the JS check).
- [ ] Player rolls `1d20 + DEX`; result appears in the shared roll log on both clients.
- [ ] Host sets `RollsVisibleToPlayers = false`. Player rolls again — result visible to roller and host only; *not* to other players. (Test with a third browser if available, or document the inferred behavior from the filter logic.)
- [ ] Host changes `TokenMovement = Anyone`; player can now move the host's NPC.
- [ ] Host clicks "End Session"; both clients return home; uploaded image files are removed from disk (verify via `ls host/KnockBox/data/plugins/dnd-mapper/`).
- [ ] **Mid-session join (§3.5 verification)**: with host + one player already in `Playing` phase, a third browser joins. Player B's token appears on the active map at the default-spawn position; their `CharacterSheet` is seeded from the current schema; host and player A both see player B's token within typical SignalR latency. (Implements GDD §4.2; owned by M01 §3.5/§3.7.)

### 5.4 Initiative tracker (v1.x — deferred)

- [ ] Confirm `ActiveCombat`, `CombatState`, `CombatantEntry`, `StartInitiativeAsync`, `SubmitInitiativeRollAsync`, `ForceInitiativeRollAsync`, `AdvanceTurnAsync`, `AddCombatantAsync`, `RemoveCombatantAsync`, `EndCombatAsync`, `InitiativeBanner.razor`, `HostInitiativePanel.razor` **DO NOT EXIST**. Run a `grep -r` for each name across the repo. Sign off on the absence; this confirms M06 didn't accidentally pull in the v1.x design.

### 5.5 Display view (v1.x — deferred)

- [ ] `DndMapperDisplay.razor` does not exist. The route `/room/dnd-mapper/{ObfuscatedRoomCode}/display` returns 404 (no observer-attach exists). Confirm via `curl`. The `IGameSessionService.AttachObserverAsync` extension does not exist on `IGameSessionService`. Confirm via grep.

### 5.6 Architecture invariant (per GDD §17.6)

- [ ] `grep -r 'using KnockBox.DndMapper' host/KnockBox/` returns no matches outside the Specs directory.
- [ ] `host/KnockBox/KnockBox.csproj` retains `ReferenceOutputAssembly="false"` and `Private="false"` on the `<ProjectReference Include="..\KnockBox.DndMapper\KnockBox.DndMapper.csproj">` line.
- [ ] `IGameEngineHttpHandler.cs` lives at `sdk/KnockBox.Core/Plugins/`; no copy in `KnockBox.Platform`.
- [ ] `host/KnockBox/Program.cs` has zero `dnd-mapper` / `DndMapper` string literals or type references.

### 5.7 Capability + analyzer compliance

- [ ] `host/KnockBox.DndMapper/plugin.json`'s `capabilities` array contains exactly `["Storage"]`. No `Network`, no `Process`, no `Environment`.
- [ ] Plugin builds without analyzer warnings KB1001–KB1004. Verify via `dotnet build` output — any KB diagnostic is a fail.

### 5.8 Lobby authoring (M06-specific)

- [ ] In the lobby phase, the host can create a map, upload an image, set it active, and adjust settings.
- [ ] Clicking Start carries the pre-authored map + active selection into the playing phase.
- [ ] Clicking Start with no maps still works — the playing phase shows an empty canvas with no `ActiveMapId`.

### 5.9 End-session

- [ ] "End Session" prompts confirmation, then disposes the state and returns all clients home.
- [ ] Storage cleanup is verified: per-room image files are gone after the session ends, both via the explicit button and via host-grace-expiry.

---

## 6. Manual verification protocol

Recommended sign-off flow:

1. Clean slate: delete `host/KnockBox/data/` and `host/KnockBox/bin/`, then `dotnet build host/KnockBox.Host.slnx -c Release`.
2. Start: `dotnet run --project host/KnockBox/KnockBox.csproj -c Release`.
3. Open two private browser windows; create + join the room.
4. Walk through §5.3's nine scenarios, ticking the matrix.
5. Walk through §5.8 and §5.9.
6. Run grep checks for §5.4, §5.5, §5.6.
7. Run unit tests (§5.2) one final time.
8. Confirm publish staging (§5.1) on a `dotnet publish -c Release`.
9. Open a PR titled `feat(dnd-mapper): v1 release — close out implementation` referencing the master plan and each milestone.

---

## 7. Files NOT to create / modify

- Anything resembling combat / initiative logic, types, or UI components.
- `DndMapperDisplay.razor`, observer-attach contracts, `/display` routes.
- Hex-grid extensions, fog-of-war masks, measurement tools, AoE templates.
- Save-format types or import/export tools.
- Mobile-specific layout overrides.
- Audio / video / soundboard components.

If any of these *do* exist in the codebase by the time M06 starts, treat that as a v1.x leak and remove before §5.4 / §5.5 sign-off.

---

## 8. Implementation choices to flag during PR

- **Lobby grid layout**: M06 puts host authoring above the player roster. If the visual hierarchy makes the lobby feel busy, collapse the authoring section into a `<details>` element ("▶ Pre-game authoring") that's expanded by default for the host.
- **Schema-change mid-session UX**: confirm modal text is critical — if the host doesn't understand the cascade, sheets get reset unexpectedly. Add an example in the modal body if testing reveals confusion.
- **End-session button placement**: on the playing-phase floating actions, top-right (away from the canvas) is recommended. Avoid putting it next to a draggable affordance.
- **Auto-cleanup on host-grace-expiry**: this is critical for §5.9. If the M03 storage cleanup hook (`state.OnStateDisposed += CleanupRoomStorage`) misfires on grace-expiry, fix it in M03 — not in M06. M06's job is to *verify*, not patch.
- **What if §5.3 fails on a scenario?** File the failure as a bug against the responsible milestone (M01–M05) and block merging M06 until it's resolved. M06 closes the loop only when every line item passes.
