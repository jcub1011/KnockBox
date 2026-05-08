# DnD Mapper — Game Design Document (DRAFT v1)

> **Status**: DRAFT, actively iterating with the user.
> **Authoritative platform reference**: [`../knockbox-platform-architecture.md`](../knockbox-platform-architecture.md).
> **Existing scaffolding (commit b96d157)**: `host/KnockBox.DndMapper/` with route identifier `"dnd-mapper"`, empty `DndMapperGameState`, scaffold `DndMapperGameEngine`, lobby page, header/tile components, matching test project. The state and engine are intentionally empty — this GDD defines what fills them.

---

## 1. Context & Vision

DnD Mapper is a **virtual tabletop (VTT)** plugin for KnockBox. Unlike the platform's other party games (Spardle, Codeword, etc.), it is open-ended: a host runs a tabletop session for a small group of players, switching between custom-built maps, placing tokens, and resolving dice rolls. There is no win condition, no round timer, and no automatic phase progression — the host drives the session start-to-finish.

The goal of v1 is to support a **single GM running a session with up to ~6 players** who can each control one character token, view a shared map, see each other's character sheets, and roll dice with attribute modifiers. The host has additional powers: building maps from uploaded images, switching the active map, placing NPC/monster tokens, and (optionally) creating extra tokens/sheets to drive other characters on a player's behalf.

This document captures the v1 design and intentionally calls out open questions for iteration. It does **not** include a build/release timeline or an implementation breakdown — those will follow once the design is locked.

---

## 2. Roles

### 2.1 Host (GM)

The host is the user who created the room. They have all player affordances **plus** map authoring and elevated token control.

- **Map management**: create, name, edit, delete maps; pick the *active map* shown to everyone; reorder maps; upload images into the active map; transform (move/scale/rotate/layer) any image; configure each map's grid.
- **Token management**: place arbitrary NPC/monster tokens on any map; place additional player-style tokens with character sheets (e.g. to run an absent player's character or pilot a DMPC); move *any* token on the board (subject to the token-movement permission setting).
- **GM rolls**: roll dice with modifiers and choose visibility (public to all, or private to host).
- **Session lifecycle**: start the session from the lobby; end the session at any time (everyone returns to the home page).
- The host's own character token + sheet is **optional** — the host is not required to participate as a character.

### 2.2 Player

A player is any non-host participant who has joined the lobby.

- Owns **exactly one** character token and **exactly one** character sheet.
- May edit their own sheet at any time during the session (subject to permission settings — see §11).
- Sees the same active map the host has selected.
- Can move their own token (default permission); cannot move other players' tokens or NPC tokens unless the host changes the permission setting.
- Can roll dice publicly (all see the result) or privately (only host sees).

### 2.3 Disconnect handling

Existing platform behaviour applies: a disconnected user has a **1-minute grace period** (managed by `ISessionServiceProvider`) before their session is torn down. While the player is disconnected their character token remains on the board. After the grace period, the player is unregistered and their token + sheet remain on the board owned by the host (so the host can decide whether to remove or keep the character).

---

## 3. Session Lifecycle

The session is **open-ended**: there is no auto-progression, round counter, or timer.

```
┌──────────┐  StartAsync   ┌──────────┐  EndSession   ┌──────────────┐
│  Lobby   │ ─────────────▶│ Playing  │──────────────▶│   Closed     │
└──────────┘               └──────────┘               └──────────────┘
   ▲                            │
   │       (no transition back) │
```

- **Lobby**: standard KnockBox lobby. Host configures session settings (default attribute preset, token-movement permission, dice-roll visibility default — see §11). Host can also pre-author maps before starting (so the session begins with a map already loaded).
- **Playing**: indefinite. Host can edit/add/switch maps, manage tokens, and toggle settings live. Players interact with the active map.
- **End**: host clicks "End Session". State is disposed via existing `GameSessionState.Dispose()` lifecycle. Uploaded images are deleted from plugin storage (see §5.5).

There is **no combat/initiative tracker in v1**. (See §15 — explicit out-of-scope.)

---

## 4. Maps

### 4.1 Map collection

A session owns a list of `Map` records. The host can author any number; only one is *active* at a time. Switching the active map is a **single host action** that flips a `ActiveMapId` field on the state and propagates to all clients via the standard `StateChangedEventManager` notification — this satisfies the user's "switch between maps quickly" requirement.

### 4.2 Per-map structure

Each `Map` contains:

| Field           | Type                       | Purpose                                                          |
| --------------- | -------------------------- | ---------------------------------------------------------------- |
| `Id`            | `Guid`                     | Stable identifier across renames/edits.                          |
| `Name`          | `string`                   | Host-chosen display name; shown in map switcher.                 |
| `Grid`          | `GridConfig` (see §6)      | Cell size, dimensions, visibility, snap mode.                    |
| `Images`        | `List<MapImage>` (see §5)  | Background/foreground images composing the map.                  |
| `Tokens`        | `List<Token>` (see §7)     | All tokens currently on this map.                                |
| `CreatedUtc`    | `DateTime`                 | For sort order in the switcher.                                  |

Tokens are **per-map**, not global — when the host switches maps, the tokens visible change. (This avoids the "where did everyone go?" UX problem of switching from a tavern interior to an outdoor wilderness map and dragging interior tokens along.) Each player's token has a "default position" that the host sets (or it auto-centers); when a map becomes active and the player has no token on it yet, one is auto-spawned at the default position.

> *Open question O-1*: alternative model — tokens are session-global and persist through map switches at their last position. Cleaner for replay-style use; messier for dungeon-vs-overworld. **Default decision: per-map tokens.**

### 4.3 Map switcher UI

Host-only sidebar listing all maps with thumbnails. Single click on a map = make active for everyone. A drag handle reorders. A "+" button creates a new empty map. Right-click (or three-dot menu) on a map: rename, duplicate, delete.

---

## 5. Map Images

### 5.1 Upload flow

The host clicks "Upload Image" in the active map's editor. The browser opens a file picker. The selected file is sent via HTTP `POST` (multipart) to a new endpoint **`POST /api/dnd-mapper/{ObfuscatedRoomCode}/images`**, registered in `Program.cs` using the standard ASP.NET minimal-api pattern guarded by:

- Authorization: caller's circuit user must equal the room's host.
- Room must exist and be in `Playing` phase (or `Lobby` if we allow pre-session authoring — see O-2).
- File size cap — proposal: **20 MB per file**, 200 MB per room total. Enforced via `FormOptions.MultipartBodyLengthLimit` plus a per-room running total tracked on state.
- MIME/content sniff: only `image/png`, `image/jpeg`, `image/webp` accepted. Reject SVG (avoid embedded-script risk).

The endpoint writes the file to plugin storage using the existing `IPluginStorage` (path-traversal-guarded). Path convention: `plugins/dnd-mapper/{room-id}/images/{guid}.{ext}`. Storage root resolved via existing `IStoragePathService.GetPluginDataDirectory("dnd-mapper")`.

After write, the endpoint returns the new `MapImage` record. The host's circuit then calls `engine.AddImageAsync(state, hostUser, mapId, mapImage)` to attach it to the active map within `state.ExecuteAsync`, which fires the state-change notification to all players.

### 5.2 Image transforms

Each `MapImage` record carries:

| Field         | Type            | Notes                                                           |
| ------------- | --------------- | --------------------------------------------------------------- |
| `Id`          | `Guid`          |                                                                 |
| `RelativePath`| `string`        | Plugin-storage path; served via §5.4.                           |
| `X`, `Y`      | `double`        | Top-left in map coordinate space (units = grid cells).          |
| `Width`, `Height` | `double`    | In grid cells (allows non-uniform scale).                       |
| `Rotation`    | `double`        | Degrees; default 0.                                             |
| `Opacity`     | `double`        | 0.0–1.0; default 1.0.                                           |
| `LayerOrder`  | `int`           | Stacking order; lowest renders first. Tokens always render above all images. |
| `Locked`      | `bool`          | If true, host's transform handles are hidden (avoids accidental drags on backgrounds). |

Transforms happen in the host's editor only. Host drags corner handles to scale, drags the body to translate, rotates via a top handle. Each transform commit (mouse-up) issues a single `engine.UpdateImageTransformAsync(...)` call; intermediate drag positions are **not** sent to the server (preserves bandwidth and avoids spamming all clients with intermediate frames).

### 5.3 Layering

Images may be stacked freely (e.g. a parchment background, a dungeon floor on top, a fog overlay on top of that). Host has a "Layers" panel listing all images on the active map with up/down reorder buttons. v1 has no folders/groups; all images live at one level.

### 5.4 Serving uploaded images

Add a controller / minimal-api endpoint **`GET /api/dnd-mapper/{ObfuscatedRoomCode}/images/{imageId}`** that:

- Verifies the caller is a member of the room (host or registered player).
- Streams the file via `IPluginStorage.OpenRead`, with appropriate `Content-Type` and `Cache-Control: private, max-age=3600` headers.

This avoids exposing the on-disk path and keeps images private to the room.

### 5.5 Image lifetime

Images are deleted from disk when the session ends (`DndMapperGameState.Dispose()` enumerates per-room image files and calls `IPluginStorage.Delete`). v1 has **no cross-session persistence** — every session starts empty. (See §15 / O-5 for the persistence open question.)

---

## 6. Grid

### 6.1 Type

v1: **square grid only**. Hex support is out of scope.

### 6.2 Per-map configuration (host editable)

| Field           | Type        | Default | Notes                                                              |
| --------------- | ----------- | ------- | ------------------------------------------------------------------ |
| `WidthCells`    | `int`       | 30      | Number of columns. 5 ≤ W ≤ 200.                                    |
| `HeightCells`   | `int`       | 20      | Number of rows. 5 ≤ H ≤ 200.                                       |
| `CellPixels`    | `int`       | 50      | Cell side in CSS pixels at 1× zoom.                                |
| `ShowGridLines` | `bool`      | true    | Players can override locally (UI-only, not server state) — TBD O-3.|
| `SnapToGrid`    | `bool`      | true    | If true, token drops snap to nearest cell center.                  |
| `LineColor`     | `string`    | "#222"  | Hex; host-configurable.                                            |

### 6.3 Coordinate system

Map space units are **grid cells** (one cell = 1.0 unit). Tokens and image positions store fractional cell coordinates; the renderer multiplies by `CellPixels` per the current zoom. This makes resizing a grid (e.g. from 20×20 to 30×30) trivial — token relative positions are preserved, just the canvas grows.

### 6.4 Pan & zoom

Each client has its own pan (px offset) and zoom (scalar). These are **client-local UI state**, not server state — every player can independently zoom in on a corner without affecting others. The host has a "Center on this cell for everyone" affordance that issues a single state-update request that nudges all clients' viewports (rare; explicit).

---

## 7. Tokens

### 7.1 Token types

| Type            | Owned by                | Sheet            | Movable by (default)                        |
| --------------- | ----------------------- | ---------------- | ------------------------------------------- |
| `PlayerToken`   | A specific `userId`     | One required     | Owner + host                                 |
| `NPCToken`      | Host (collective)       | None or simple   | Host only                                    |
| `HostExtraToken`| Host (collective), but flagged "represents player X" optionally | Optional | Host only                                    |

Per the user's role decision (§2): players own exactly one `PlayerToken` and one sheet. The host can spawn any number of `NPCToken`s (no sheet) or `HostExtraToken`s (with optional sheet, useful for piloting absent players' characters or running DMPCs).

### 7.2 Token record

| Field         | Type            | Notes                                                                                  |
| ------------- | --------------- | -------------------------------------------------------------------------------------- |
| `Id`          | `Guid`          |                                                                                        |
| `Type`        | enum            | `PlayerToken` / `NPCToken` / `HostExtraToken`.                                          |
| `OwnerUserId` | `string?`       | Set for `PlayerToken`; null otherwise. Used to enforce "move own" permission.           |
| `Name`        | `string`        | Player display name by default for `PlayerToken`; host-chosen otherwise.                |
| `Color`       | `string`        | Border/highlight color. Player picks for own token; host picks for NPC.                 |
| `IconKind`    | `enum`          | v1: `Initial` (first letter of Name) or `Solid` colored disc. Image upload — see O-4.   |
| `MapId`       | `Guid`          | The map this token currently lives on.                                                  |
| `X`, `Y`      | `double`        | Cell coordinates.                                                                       |
| `SheetId`     | `Guid?`         | Reference to a `CharacterSheet` record (see §8). Required for `PlayerToken`; optional otherwise. |
| `Hidden`      | `bool`          | If true, only the host sees the token. (Useful for not-yet-revealed monsters.)          |

### 7.3 Movement

Token drag uses an SVG-based interaction modeled on the existing `outfitItemDrag.js` pattern (mouse + touch, viewBox-aware). On drag-end:

1. Client computes target cell (snapped if `SnapToGrid`).
2. Client invokes `engine.MoveTokenAsync(state, user, tokenId, x, y)`.
3. Engine validates the **token-movement permission setting** (§11) against `(user, token.OwnerUserId, token.Type)`:
   - `OwnerOrHost` (default): user must equal `token.OwnerUserId`, or be host.
   - `Anyone`: any participant may move any token.
   - `HostOnly`: only the host may move any token.
4. Engine mutates inside `state.ExecuteAsync` and the change broadcasts.

**Intermediate drag positions are not sent to the server** in v1 — only the final drop. (Real-time "ghost" of where another player is dragging is a nice-to-have; see §15.)

> *Open question O-6*: should there be a per-token-move animation (linear interpolation client-side from old → new) so other players see the token glide rather than teleport? Probably yes for polish; spec it under §10.

### 7.4 Multiple tokens for the host

The host's "Tokens" panel lists all NPC and host-extra tokens with a "+ Add" button. Adding spawns one at the center of the active map. Each is editable (name, color, sheet attach/detach, hidden flag, delete).

---

## 8. Character Sheets (Attributes)

### 8.1 Schema selection

The host picks one of the following at session start (lobby) and may re-pick during the session (with a confirmation, since changing the schema invalidates existing attribute values):

| Preset           | Attributes                                                                                | Default? |
| ---------------- | ----------------------------------------------------------------------------------------- | -------- |
| **D&D 5e Core**  | STR, DEX, CON, INT, WIS, CHA (each: ability score 1–30; modifier auto-derived `(score-10)/2`) | ✅ default |
| **D&D 5e + Common Skills** | All six abilities + Athletics, Stealth, Perception, Persuasion, Investigation (skill modifiers, separate from abilities) | |
| **Simple d20** | Single "Modifier" attribute. Useful for one-shot pickup play.                             | |
| **Custom**       | Host defines a list of `(name, type, default)` rows. Type ∈ {`Modifier (int)`, `Score (int 1-30, auto-modifier)`, `Text`}. | |

The chosen schema lives on `DndMapperGameState.AttributeSchema` and is broadcast to all clients. Each player's sheet stores values keyed by attribute name.

### 8.2 Sheet record

| Field         | Type                                  | Notes                                                                                |
| ------------- | ------------------------------------- | ------------------------------------------------------------------------------------ |
| `Id`          | `Guid`                                |                                                                                      |
| `OwnerUserId` | `string?`                             | Player user-id, or null for host-pilot sheets.                                       |
| `CharacterName` | `string`                            | Display name on the sheet (separate from KnockBox display name).                     |
| `Values`      | `Dictionary<string, AttributeValue>`  | Keyed by attribute name from the schema. `AttributeValue` is a discriminated union (int score, int modifier, text). |
| `Notes`       | `string`                              | Free-text notes the owner can edit.                                                  |
| `Hp`, `MaxHp` | `int?`                                | Optional v1 hit-point tracking (independent of schema). Visible to host always; visibility to others — TBD O-7. |

### 8.3 Editing & visibility

- A player edits their **own** sheet via a panel in the side rail.
- The host can edit **any** sheet (they're acting as GM).
- Sheets are visible to all participants by default — character names + attributes are public to the table. (Notes and HP — see O-7.) This matches in-person tabletop convention; players who want secret stats are uncommon.

### 8.4 Where the sheet lives in state

`CharacterSheet`s are **session-scoped, not map-scoped** (a character has the same stats regardless of which map you're on). Stored on `DndMapperGameState.Sheets : Dictionary<Guid, CharacterSheet>` with `Token.SheetId` referencing them.

---

## 9. Dice Rolling

### 9.1 Supported dice

Standard polyhedral set: **d4, d6, d8, d10, d12, d20, d100**. Up to 20 dice per roll. Each roll may include a flat modifier, an attribute reference, advantage, or disadvantage.

### 9.2 Roll request shape

```
RollRequest {
    Dice: [(Count, Sides), …],           // e.g. (1, 20), (2, 6)
    AttributeRef: (sheetId, attrName)?,  // optional — adds the attribute's modifier
    FlatModifier: int,                    // additive; default 0
    Mode: enum { Normal, Advantage, Disadvantage },
    Visibility: enum { Public, PrivateToHost, PrivateToSelf },
    Label: string                         // free text, e.g. "Stealth check"
}
```

### 9.3 Engine handling

`engine.RollAsync(state, user, request)`:

1. Validates the user owns the referenced sheet, or is host.
2. Calls existing `IRandomNumberService.GetRandomInt(1, sides+1, RandomType.Fast)` once per die. (Reuses the platform service the DiceSimulator plugin already uses.)
3. Computes total: sum of dice (with adv/dis applied to a single d20 if Mode ≠ Normal), plus flat modifier, plus resolved attribute modifier.
4. Records a `RollResult` in `DndMapperGameState.RollLog : List<RollResult>` (capped at 200 most recent).
5. Visibility rules determine which clients render the result.

### 9.4 UI

A bottom-right "Roll" button opens a modal with:
- Quick buttons (`d20`, `2d6`, `Initiative`).
- A dropdown to attach an attribute from the user's sheet (auto-fills the modifier).
- Adv/Dis toggles.
- Visibility selector (default = `Public`).
- A history strip showing the last several rolls (chat-like log).

Public rolls broadcast to all; private-to-host rolls render only on the host's screen and the rolling player's screen; private-to-self rolls render only on the rolling player's screen.

---

## 10. Real-time Synchronization

The platform's standard pattern applies (per `knockbox-platform-architecture.md`):

- All mutations go through `state.Execute` / `state.ExecuteAsync`.
- Each Razor page subscribes via `state.StateChangedEventManager.Subscribe(...)` and disposes the subscription via `DisposableComponent`.
- Notification fires *after* the lock is released, so handlers can safely re-enter the engine.

VTT-specific concerns:

- **Token drag broadcasts only the final drop** (per §7.3) — no per-frame updates. This keeps SignalR traffic bounded.
- **Image upload + transform** broadcasts on commit (mouse-up), not during drag.
- **Pan/zoom is client-local**, never broadcast.
- **Animation polish (O-6)**: clients may smoothly tween a token from its previous to new position over ~150 ms when they receive a token-move state change.

If we hit perceptible jank under high churn (e.g. host bulk-importing 20 NPC tokens), we can introduce a coalescing wrapper around `StateChangedEventManager.Notify` that batches notifications within a 16 ms window — but this is **not** required for v1.

---

## 11. Permissions

Configurable session settings, set in lobby and editable mid-session by the host:

| Setting                       | Options                                                | Default          |
| ----------------------------- | ------------------------------------------------------ | ---------------- |
| `TokenMovement`               | `OwnerOrHost` / `Anyone` / `HostOnly`                  | `OwnerOrHost`    |
| `SheetEditByOthers`           | `OwnersOnly` / `OwnersAndHost` / `Anyone`              | `OwnersAndHost`  |
| `DefaultRollVisibility`       | `Public` / `PrivateToHost` / `PrivateToSelf`            | `Public`         |
| `PlayersCanCreateNPCs`        | `bool`                                                 | `false`          |
| `MapEditByPlayers`            | `bool`                                                 | `false` (host-only authoring in v1) |

These live on `DndMapperGameState.Settings`.

---

## 12. State Model Sketch

```csharp
public sealed class DndMapperGameState : AbstractGameState
{
    public DndMapperPhase Phase { get; private set; }              // Lobby | Playing
    public DndMapperSettings Settings { get; private set; }
    public AttributeSchema AttributeSchema { get; private set; }

    public List<Map> Maps { get; }                                 // host-authored
    public Guid? ActiveMapId { get; private set; }

    public Dictionary<Guid, CharacterSheet> Sheets { get; }        // session-scoped
    public List<RollResult> RollLog { get; }                       // capped to 200

    // Tokens live on Map.Tokens — not duplicated here.

    // All mutators are private; engine calls Execute(...) helpers.
}
```

Engine methods (verbs):

- **Maps**: `CreateMapAsync`, `RenameMapAsync`, `DeleteMapAsync`, `DuplicateMapAsync`, `SetActiveMapAsync`, `UpdateGridAsync`.
- **Images**: `AddImageAsync` (called by upload endpoint after disk write), `UpdateImageTransformAsync`, `ReorderImageLayerAsync`, `RemoveImageAsync`.
- **Tokens**: `SpawnPlayerTokenAsync` (auto on first map activation), `SpawnNpcTokenAsync`, `SpawnHostExtraTokenAsync`, `MoveTokenAsync`, `UpdateTokenAsync` (rename/recolor/icon), `RemoveTokenAsync`, `SetTokenHiddenAsync`.
- **Sheets**: `CreateSheetAsync`, `UpdateSheetAttributeAsync`, `UpdateSheetFreeFieldsAsync` (name/notes/hp), `DeleteSheetAsync`, `ChangeSchemaAsync`.
- **Dice**: `RollAsync`.
- **Settings**: `UpdateSettingsAsync`.
- **Lifecycle**: `EndSessionAsync`.

Each verb is a small `Execute` block guarded by a permission check up front. Test fixtures mirror Spardle's structure (one per verb under `Unit/Logic/Games/DndMapper/`).

---

## 13. UI Pages

```
host/KnockBox.DndMapper/Pages/
    DndMapperLobby.razor                  (already exists, scaffold)
    DndMapperRoom.razor                   (NEW — entry; switches on Phase)
    DndMapperPlayingPhase.razor           (NEW — main play canvas)
        Components/
            MapCanvas.razor               (SVG: images + grid + tokens)
            TokenLayer.razor              (interactive, uses outfitItemDrag.js style interop)
            HostMapSwitcher.razor         (host-only sidebar)
            HostImageInspector.razor      (host-only transform handles)
            CharacterSheetPanel.razor     (per-player; switches between own/others)
            DiceRollerModal.razor
            RollLogPanel.razor
            PermissionsPanel.razor        (host-only; mirrors §11)
```

`DndMapperRoom.razor` follows the Spardle phase-switch convention. Pages inherit `DisposableComponent` and subscribe to `state.StateChangedEventManager`. The `@page` route is `/room/dnd-mapper/{ObfuscatedRoomCode}` (matches the route identifier in `plugin.json`, per the platform invariant).

---

## 14. Reused Platform Utilities

| Need                          | Existing utility                                                                              |
| ----------------------------- | --------------------------------------------------------------------------------------------- |
| Dice RNG                      | `IRandomNumberService` (`sdk/KnockBox.Platform/Services/Logic/RandomGeneration/`).            |
| Per-plugin file storage paths | `IStoragePathService` / `DefaultStoragePathService`.                                          |
| Read/write/delete files       | `IPluginStorage` / `DefaultPluginStorage` (path-traversal-guarded).                           |
| State concurrency + sync      | `AbstractGameState.Execute*` / `StateChangedEventManager`.                                    |
| Lobby base                    | `LobbyPageBase<TState>` (already used by `DndMapperLobby.razor`).                             |
| Result types                  | `Result` / `ValueResult<T>` / `ValueResult<T, TError>` for fallible engine ops.                |
| Drag interop reference        | `host/KnockBox/wwwroot/js/outfitItemDrag.js` (SVG viewBox math + DotNetObjectReference callback). |
| Disposable component base     | `DisposableComponent` from `KnockBox.Core`.                                                   |
| Phase-switch razor pattern    | `host/KnockBox.Spardle/Pages/SpardleRoom.razor`.                                              |
| Per-player attribute pattern  | `host/KnockBox.HiddenAgenda/Services/State/Games/Data/HiddenAgendaPlayerState.cs` (richer per-player records). |

**Net new infrastructure**: image upload HTTP endpoint, image serving HTTP endpoint, square-grid SVG component, token drag interop (adapt `outfitItemDrag.js`), dice modal + roll log component, attribute-schema editor, map switcher.

---

## 15. Out of MVP Scope (Explicit)

These are intentionally excluded from v1 to keep the design scoped. Any of them can be revisited as v1.x increments.

- **Initiative tracker / combat mode** — open-ended sessions only.
- **Fog of war / vision masking** — host-controlled visibility regions.
- **Hex grids**.
- **Measurement tools** (ruler, AoE templates, line-of-sight).
- **Token uploaded artwork** — v1 uses initial-letter or solid-disc icons (O-4).
- **Map cross-session persistence** — every session starts empty (O-5).
- **Real-time drag previews** — only final drop is broadcast.
- **Audio / video chat / soundboard**.
- **Multi-host / co-GM**.
- **Macros and rollable tables**.
- **Import/export of campaigns** (no save format).
- **Mobile-optimized layout** — desktop-first.

---

## 16. Open Questions (Iterate with User)

| ID  | Question                                                                                                                                                  | Current default                |
| --- | --------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------ |
| O-1 | Tokens per-map vs session-global with last-position memory?                                                                                                | Per-map.                       |
| O-2 | Can the host upload images / author maps **during the lobby phase** before starting? (Useful for prep.) Or is image upload Playing-only?                   | Lobby authoring allowed.       |
| O-3 | Player override of grid-line visibility — local UI toggle, or strictly host-controlled?                                                                    | Local UI toggle.               |
| O-4 | Token icons — v1 limited to letter/solid-disc, or also allow per-token uploaded portraits? (Adds upload + storage for tokens, similar to map images.)      | Letter / solid disc only in v1.|
| O-5 | Cross-session persistence — should a host be able to save a map collection and re-open it next session?                                                    | No, ephemeral.                 |
| O-6 | Token-move animation — server is final-position only; should clients tween to smooth the visual?                                                           | Yes, ~150 ms tween.            |
| O-7 | Sheet HP and Notes visibility — public to all, host-only, or owner-and-host?                                                                              | Owner + host.                  |
| O-8 | Per-room storage cap (proposed 200 MB) and per-image cap (20 MB) — too high, too low, configurable in `appsettings.json`?                                  | 20 MB / image, 200 MB / room.  |
| O-9 | Should the dice roll log include all rolls in the session (even private ones, host-only) or only the rolls the viewer is permitted to see?                | Viewer-permission filtered.    |
| O-10| Hidden NPC tokens — does the host also see them differently (e.g. ghosted) so they don't forget?                                                          | Yes, ghosted with eye-slash.   |

---

## 17. Verification (when implementation begins)

When the implementation phase starts, the following validation matrix should be run:

1. **Build**: `dotnet build host/KnockBox.Host.slnx` succeeds; `KnockBox.DndMapper.dll` is staged into `host/KnockBox/bin/{Config}/{TFM}/games/KnockBox.DndMapper/`.
2. **Unit tests**: `dotnet test host/KnockBox.DndMapperTests/KnockBox.DndMapperTests.csproj` passes for every engine verb — happy path + permission rejection + invalid-input path.
3. **End-to-end**: with the host running, two browsers (host + one player) join the same room. Verify:
   - Host uploads an image; both clients see it on the active map.
   - Host transforms the image; both clients see the new position/scale.
   - Host creates a second map; switcher updates; switching active map propagates to player.
   - Player drags own token; host sees move within the SignalR latency budget.
   - Player tries to drag host's NPC token under default permissions — blocked.
   - Player rolls 1d20 + DEX modifier; result appears in shared log.
   - Host changes `TokenMovement` to `Anyone`; player can now move NPC.
   - Host clicks "End Session"; both clients return home; uploaded image files are removed from disk.
4. **Architecture invariant**: `KnockBox.csproj` still has zero `using` of `KnockBox.DndMapper` types; `ReferenceOutputAssembly="false"` on the project ref is preserved.

---

*End of v1 draft. Ready for iteration — pick any section to refine or flip any open-question default and the doc will be updated in place.*
