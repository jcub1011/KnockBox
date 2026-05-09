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
- **GM rolls**: roll dice with modifiers; the host always sees every roll result regardless of session settings.
- **Session lifecycle**: start the session from the lobby; end the session at any time (everyone returns to the home page).
- The host's own character token + sheet is **optional** — the host is not required to participate as a character.
- **View (v1)**: the host's main tab is the **control view** — full GM interface with hidden tokens visible, all rolls visible, all management panels. The screen-shareable **display view** at `/room/dnd-mapper/{ObfuscatedRoomCode}/display` is **deferred to v1.x** (see §13).

### 2.2 Player

A player is any non-host participant who has joined the lobby.

- Owns **one `PlayerToken` per map they have appeared on** and **exactly one session-scoped character sheet** (shared across all of their per-map tokens — see §7.1, §8.4).
- May edit their own sheet at any time during the session (subject to permission settings — see §11).
- Sees the same active map the host has selected.
- Can move their own token (default permission); cannot move other players' tokens or NPC tokens unless the host changes the permission setting.
- Can roll dice. Whether other players see each other's roll results is controlled by the host's `RollsVisibleToPlayers` session setting (see §11).

### 2.3 Disconnect handling

Existing platform behaviour applies: a disconnected user has a **1-minute grace period** (managed by `ISessionServiceProvider`) before their session is torn down. While the player is disconnected their character tokens remain on the board.

**Player unregistration (post-grace).** When `PlayerUnregistered` fires for a player, the engine handler runs inside `state.ExecuteAsync` and:

1. Iterates every `PlayerToken` owned by the player. Each is converted in place: `Type` → `HostExtraToken`, `OwnerUserId` → `null`, `RepresentsUserId` → the departing player's userId. The token's position, sheet reference, name, and color are preserved. This means the host (and only the host, under default `OwnerOrHost`) can move the abandoned character; the display still attributes it to the original player.
2. The character sheet is left intact (still in `Sheets`, still referenced by the converted tokens). The host may delete it via `DeleteSheetAsync` if desired.

**Player rejoin after grace expiry.** A returning user is treated as a fresh mid-session join: `SpawnPlayerTokenAsync` runs on the active map, producing a new `PlayerToken` and a new `CharacterSheet`. The previously-converted `HostExtraToken`(s) and old sheet remain for the host to keep, repurpose, or delete — they are not auto-reclaimed.

**Host disconnect.** The host's circuit also has a 1-minute grace window. While disconnected the session continues and players can still interact with the map. If the host does not return within grace, the engine runs `EndSessionAsync` and everyone returns home.

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

- **Lobby**: standard KnockBox lobby. Host configures session settings (attribute preset, token-movement permission — see §11). Host can also pre-author maps before starting (so the session begins with a map already loaded).
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
| `CreatedUtc`    | `DateTime`                 | Default sort fallback when `ListOrder` is unset.                 |
| `ListOrder`     | `int`                      | Manual order index assigned by the host (drag-reorder in §4.3). New maps append at the next free index. The switcher always sorts by `ListOrder` ascending; `CreatedUtc` is only consulted as a tiebreak. |
| `DefaultSpawnPosition` | `(double X, double Y)?` | Cell coordinates where player tokens are placed when auto-spawned on this map. If null, the system uses the map center. |

Tokens are **per-map**, not global — when the host switches maps, the tokens visible change. (This avoids the "where did everyone go?" UX problem of switching from a tavern interior to an outdoor wilderness map and dragging interior tokens along.) Each Map has an optional `DefaultSpawnPosition` (grid cell coordinates). When a player's token does not yet exist on a map that becomes active, `SpawnPlayerTokenAsync` places it at that position (or at the map center if unset). Token and sheet records are created for all lobby players when the session starts; players joining mid-session have their token spawned immediately on the active map.

> **Decision (O-1, closed)**: tokens are per-map. The session-global alternative was considered (cleaner for replay-style use, messier for dungeon-vs-overworld) and rejected. See §16.

### 4.3 Map switcher UI

Host-only sidebar listing all maps with thumbnails. Single click on a map = make active for everyone. A drag handle reorders. A "+" button creates a new empty map. Right-click (or three-dot menu) on a map: rename, duplicate, delete.

---

## 5. Map Images

### 5.1 Upload flow

The host clicks "Upload Image" in the active map's editor. The browser opens a file picker. The selected file is sent via HTTP `POST` (multipart) to **`POST /api/plugins/dnd-mapper/{ObfuscatedRoomCode}/images`**, served by a generic plugin-route dispatcher in `KnockBox.Platform` (registered via `MapKnockBoxPlatformEndpoints`). The dispatcher resolves the plugin engine via keyed DI on `{routeIdentifier}` and delegates to a new SDK-level `IGameEngineHttpHandler` contract that `DndMapperGameEngine` opts into — `Program.cs` has no plugin-specific knowledge, preserving the host↔plugin compile-time isolation invariant. The dispatcher enforces:

- Authorization: caller's circuit user must equal the room's host.
- Room must exist and be in `Playing` or `Lobby` phase (lobby authoring is supported).
- File size cap: **5 MB per file**, 10 MB per room total. Enforced via `FormOptions.MultipartBodyLengthLimit` plus a per-room running total tracked on state.
- MIME/content sniff: only `image/png`, `image/jpeg`, `image/webp` accepted. Reject SVG (avoid embedded-script risk).

The endpoint writes the file to plugin storage using the existing `IPluginStorage` (path-traversal-guarded). Path convention: `{room-id}/images/{guid}.{ext}` (relative to the plugin storage root). Storage root resolved via existing `IStoragePathService.GetPluginDataDirectory("dnd-mapper")`.

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

The same platform dispatcher serves **`GET /api/plugins/dnd-mapper/{ObfuscatedRoomCode}/images/{imageId}`** through `IGameEngineHttpHandler`:

- Verifies the room exists. **No further auth check** — the obfuscated room code (two random GUIDs in the URL) is treated as the access token, matching the existing room-URL convention. Keeps the v1 player view simple and leaves the door open for the v1.x display view (§13) to load images without per-circuit auth.
- Streams the file via `IPluginStorage.OpenRead`, with appropriate `Content-Type` and `Cache-Control: private, max-age=3600` headers.

This avoids exposing the on-disk path and keeps images discoverable only to clients who know the room URL.

### 5.5 Image lifetime

Images are deleted from disk on three triggers, each running through the engine inside `state.ExecuteAsync`:

1. **Per-image removal** (`RemoveImageAsync`): the file at `MapImage.RelativePath` is deleted via `IPluginStorage.Delete`, and the per-room running total used by the §5.1 cap is decremented by the file's byte size.
2. **Map deletion** (`DeleteMapAsync`): cascades to every `MapImage` on the map (same delete + decrement as above) before the `Map` record is removed. Tokens on the map are removed from state but have no associated disk artifacts.
3. **Session end** (`DndMapperGameState.Dispose()`): enumerates any remaining per-room image files under the plugin's storage root and calls `IPluginStorage.Delete` for each.

v1 has **no cross-session persistence** — every session starts empty. (See §15 — save/reload of campaigns is planned as a future feature.)

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
| `ShowGridLines` | `bool`      | true    | Players can override locally via a UI toggle (client-only state, not server state). |
| `SnapToGrid`    | `bool`      | true    | If true, token drops snap to nearest cell center.                  |
| `LineColor`     | `string`    | "#222"  | Hex; host-configurable.                                            |

### 6.3 Coordinate system

Map space units are **grid cells** (one cell = 1.0 unit). Tokens and image positions store fractional cell coordinates; the renderer multiplies by `CellPixels` per the current zoom. This makes resizing a grid (e.g. from 20×20 to 30×30) trivial — token relative positions are preserved, just the canvas grows.

### 6.4 Pan & zoom

Each client has its own pan (px offset) and zoom (scalar). These are **client-local UI state**, not server state — every player can independently zoom in on a corner without affecting others. (A "center viewport for everyone" broadcast affordance was considered and is deferred to v1.x — see §15.)

---

## 7. Tokens

### 7.1 Token types

| Type            | Owned by                | Sheet            | Movable by (default)                        |
| --------------- | ----------------------- | ---------------- | ------------------------------------------- |
| `PlayerToken`   | A specific `userId`     | One required     | Owner + host                                 |
| `NPCToken`      | Host (collective) by default; if `PlayersCanCreateNPCs = true`, the creating player's `userId` is assigned as `OwnerUserId`. | None or simple | Host always; creating player if `PlayersCanCreateNPCs = true` (subject to `TokenMovement` setting). |
| `HostExtraToken`| Host (collective), but flagged "represents player X" optionally | Optional | Host only                                    |

Per the role decision in §2.2: a player owns **one `PlayerToken` per map they have appeared on** (auto-spawned by `SetActiveMapAsync` when the player has no token on the newly active map — see §4.2 and §12) and **exactly one session-scoped `CharacterSheet`** (§8.4). All of that player's per-map tokens reference the same `SheetId`, so attribute changes propagate everywhere. The host can spawn any number of `NPCToken`s (no sheet) or `HostExtraToken`s (with optional sheet, useful for piloting absent players' characters or running DMPCs).

### 7.2 Token record

| Field         | Type            | Notes                                                                                  |
| ------------- | --------------- | -------------------------------------------------------------------------------------- |
| `Id`          | `Guid`          |                                                                                        |
| `Type`        | enum            | `PlayerToken` / `NPCToken` / `HostExtraToken`.                                          |
| `OwnerUserId` | `string?`       | Set for `PlayerToken`; null otherwise (or creating player's userId for NPCs when `PlayersCanCreateNPCs = true`). Used to enforce "move own" permission. |
| `RepresentsUserId` | `string?`  | Set on `HostExtraToken` when piloting an absent player's character; null otherwise. Display-only — does not affect permission checks. |
| `Name`        | `string`        | Player display name by default for `PlayerToken`; host-chosen otherwise.                |
| `Color`       | `string`        | Border/highlight color. **Default by token type**: `PlayerToken` — deterministic palette color assigned by player slot index (0-based, cycling through a fixed 8-color palette); `HostExtraToken` — the slot color of the player named by `RepresentsUserId` if set, else neutral grey (`#888`); `NPCToken` — neutral grey (`#888`). Player can override for own token; host picks for NPC/extra tokens. |
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

> **Decision (O-6, closed)**: clients tween a token from its previous to new position over ~150 ms when they receive a token-move state change. See §10 for the animation spec; §16 for the closed-question record.

### 7.4 Multiple tokens for the host

The host's "Tokens" panel lists all NPC and host-extra tokens with a "+ Add" button. Adding spawns one at the center of the active map. Each is editable (name, color, sheet attach/detach, hidden flag, delete).

---

## 8. Character Sheets (Attributes)

### 8.1 Schema selection

The host picks one of the following at session start (lobby) and may re-pick during the session. Re-picking mid-session opens a **host-only confirmation modal** (no player vote / approval) describing the cascade described below. On schema change: attributes whose name exists in both the old and new schema retain their value (type mismatch resets to default); attributes absent from the new schema are silently removed from all sheets.

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
| `Hp`, `MaxHp` | `int?`                                | Optional v1 hit-point tracking (independent of schema). Visible to owner and host only; other players do not see HP values. Both are nullable — if unset, the HP row is hidden in the sheet panel. Edited via +/− stepper buttons (direct numeric entry also allowed). |

### 8.3 Editing & visibility

- A player edits their **own** sheet via a panel in the side rail.
- The host can edit **any** sheet (they're acting as GM). The host is always exempt from `SheetEditByOthers` — they can edit any sheet regardless of the setting.
- Character names and attribute values are visible to all participants (matching in-person tabletop convention). Notes and HP are visible to the sheet owner and host only.

### 8.4 Where the sheet lives in state

`CharacterSheet`s are **session-scoped, not map-scoped** (a character has the same stats regardless of which map you're on). Stored on `DndMapperGameState.Sheets : Dictionary<Guid, CharacterSheet>` with `Token.SheetId` referencing them.

---

## 9. Dice Rolling

### 9.1 Supported dice

Standard polyhedral set: **d4, d6, d8, d10, d12, d20, d100**. The total dice count summed across all `(Count, Sides)` entries in a single request is capped at **20** (e.g. `[(20,6)]` is allowed; `[(20,6),(1,20)]` is rejected at validation). Each roll may include a flat modifier, an attribute reference, advantage, or disadvantage.

### 9.2 Roll request shape

```
RollRequest {
    Dice: [(Count, Sides), …],           // e.g. (1, 20), (2, 6)
    AttributeRef: (sheetId, attrName)?,  // optional — adds the attribute's modifier
    FlatModifier: int,                    // additive; default 0
    Mode: enum { Normal, Advantage, Disadvantage },
    Label: string                         // free text, e.g. "Stealth check"
}
```

### 9.3 Engine handling

`engine.RollAsync(state, user, request)`:

1. Validates the user owns the referenced sheet, or is host.
2. Validates the dice cap (§9.1) and the adv/dis precondition: when `Mode ≠ Normal`, `Dice` must contain exactly one entry of `(1, 20)` — otherwise the call is rejected. (Adv/dis is a d20-only mechanic; silently ignoring or coercing to the largest die would surprise users.)
3. Calls existing `IRandomNumberService.GetRandomInt(1, sides+1, RandomType.Fast)` once per die. (Reuses the platform service the DiceSimulator plugin already uses.)
4. Computes total: sum of dice (with adv/dis applied to the single d20 when Mode ≠ Normal), plus flat modifier, plus resolved attribute modifier.
5. Records a `RollResult` in `DndMapperGameState.RollLog : List<RollResult>` (capped at 200 most recent). Each `RollResult` carries `RollerUserId : string` (the participant the roll is *attributed* to). The `ForcedByUserId : string?` field (set when the host force-rolled on someone else's behalf — see §9.5.3 step 4) is reserved for the v1.x initiative tracker; in v1 it is always `null`.
6. The full `RollLog` is broadcast to all clients via the standard state-change notification. The host always renders all results. Non-host clients render a result if `Settings.RollsVisibleToPlayers = true` OR `roll.RollerUserId == currentUserId`. **Note: this filtering is client-side** — the full log is on the wire. Server-side per-user diffing was considered and judged not worth the complexity for v1; the threat model is "casual friends playing a tabletop," not "adversarial competitors with custom clients."

### 9.4 UI

A bottom-right "Roll" button opens a modal with:
- Quick buttons (`d20`, `2d6`, `Initiative`).
- A dropdown to attach an attribute from the user's sheet (auto-fills the modifier).
- Adv/Dis toggles.
- A history strip showing the last 20 rolls the current viewer is permitted to see (chat-like log). Non-host players see only their own rolls when `RollsVisibleToPlayers = false`.

---

## 9.5 Initiative Tracker

> **Scope**: deferred to v1.x. The §9.5 design is preserved here for the v1.x increment; v1 ships without combat / turn order. References elsewhere in this doc — `ActiveCombat` state field, combat engine verbs (§12), `InitiativeBanner.razor`, `HostInitiativePanel.razor` (§13), and the `ForcedByUserId` field on `RollResult` (§9.3) — are likewise v1.x.

### 9.5.1 Overview

The initiative tracker is an optional mode the host activates to manage turn-based combat. It does not alter the open-ended session structure; the host starts and stops combat at will, as many times per session as needed. When inactive, the entire initiative UI is hidden from all clients.

### 9.5.2 Combat State

```
CombatState {
    Phase:             WaitingForRolls | Active
    RoundNumber:       int                     // 1-based; increments each time the order wraps
    CurrentTurnIndex:  int                     // index into TurnOrder (valid only in Active phase)
    TurnOrder:         List<CombatantEntry>    // sorted descending by roll when Phase → Active
}

CombatantEntry {
    Id:              Guid       // unique within this combat instance
    TokenId:         Guid       // reference to the on-map token (for name / color)
    Name:            string     // denormalized from token at combat start
    OwnerUserId:     string?    // non-null for PlayerToken combatants
    InitiativeRoll:  int?       // null = not yet rolled (WaitingForRolls only)
    IsForceRolled:   bool       // true if the host force-rolled on behalf of this combatant
}
```

`DndMapperGameState.ActiveCombat : CombatState?` — null when no combat is running.

**Tiebreak rule**: equal initiative rolls sort players before NPCs; within each group, alphabetical by `Name`.

### 9.5.3 Initiative Prompt Flow

1. **Host starts initiative.** Host opens a checklist of all NPC tokens on the active map and selects combatants (zero or more — a PvP-only fight with no NPCs is allowed; the only minimum is that the resulting `TurnOrder` must contain ≥ 1 combatant after auto-adding all registered players). Calls `StartInitiativeAsync(state, host, npcTokenIds[])`:
   - Creates `ActiveCombat` in `WaitingForRolls`, `RoundNumber = 1`.
   - Creates a `CombatantEntry` for each selected NPC (`InitiativeRoll = null`).
   - Creates a `CombatantEntry` for every registered player (`InitiativeRoll = null`).
   - State notification fires; all clients show the initiative prompt banner.

2. **Players roll.** Each player sees a "Roll Initiative!" button in the `InitiativeBanner`. Clicking calls `SubmitInitiativeRollAsync(state, user)`, which rolls a d20 via `IRandomNumberService` + the player's DEX modifier (if their sheet has one), appends a public `RollResult` to `RollLog` labelled "Initiative", and stores the total in the player's `CombatantEntry.InitiativeRoll`.

3. **Host enters NPC initiative.** The host's initiative panel shows each NPC with a "Roll" button (auto-generates d20) and an editable numeric field. `SetNpcInitiativeAsync(state, host, combatantId, roll)` stores the value.

4. **Force-roll for missing players.** If a player hasn't rolled, the host can click "Force Roll" next to their name: `ForceInitiativeRollAsync(state, host, combatantId)` generates a d20 + modifier and appends a public `RollResult` with `RollerUserId = absent player's userId` (so the player sees "their" roll under default-private settings on reconnect) and `ForcedByUserId = host's userId`. The display label "Initiative (forced by GM)" is derived purely from the non-null `ForcedByUserId` flag — the engine does not need to write the label string anywhere. `CombatantEntry.IsForceRolled` is set to true.

5. **Automatic transition to Active.** When every `CombatantEntry.InitiativeRoll` is non-null, the engine sorts `TurnOrder` descending by roll (tiebreak applied), sets `Phase = Active`, `CurrentTurnIndex = 0`, and fires the state notification. The banner updates to the turn-order view for all clients.

### 9.5.4 Turn Progression

- **Next Turn**: `AdvanceTurnAsync(state, host)` increments `CurrentTurnIndex`. When it exceeds `TurnOrder.Count − 1`, it wraps to 0 and increments `RoundNumber`. All clients see the new current combatant highlighted.
- **Skipping**: there is no separate skip verb — skipping is the host pressing "Next Turn" when it is the unwanted combatant's turn. The current combatant acts or is passed over at the host's discretion.

### 9.5.5 Mid-Combat Actions

- **Add combatant**: `AddCombatantAsync(state, host, tokenId, initiativeRoll)` inserts a new `CombatantEntry` at the initiative-roll position in `TurnOrder`. If the insertion index ≤ `CurrentTurnIndex`, the engine **increments `CurrentTurnIndex`** so it continues to reference the same combatant who is currently acting; the inserted combatant's first turn is therefore on the next pass through the order (next round if they landed before the current pointer). If the insertion index > `CurrentTurnIndex`, no pointer adjustment is needed and the inserted combatant acts later in the same round.
- **Remove combatant**: `RemoveCombatantAsync(state, host, combatantId)` deletes the entry. If the removed entry was the current turn, the engine auto-advances to the next valid combatant (wrapping and incrementing `RoundNumber` if necessary).
- **End combat**: `EndCombatAsync(state, host)` sets `ActiveCombat = null`. The banner disappears for all clients.

### 9.5.6 UI Components

`InitiativeBanner.razor` is rendered on all clients (players, host control view, display view) whenever `ActiveCombat != null`:

| Phase | What all clients see |
|---|---|
| `WaitingForRolls` | Each combatant row showing name and roll status (✓ rolled / — not rolled). Player-controlled combatants who haven't rolled see a prominent "Roll Initiative!" button inline. |
| `Active` | Full ordered list: combatant names, roll totals, round number. Current combatant highlighted. |

`HostInitiativePanel.razor` (host control view only) provides:

- **Pre-combat**: "Start Initiative" button that opens the NPC selection checklist.
- **WaitingForRolls**: NPC roll entry fields with "Roll" buttons; "Force Roll" button per unrolled player.
- **Active**: "Next Turn" button; "Add Combatant" (enter token + roll); "Remove" button per combatant row; "End Combat" button.

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
| `RollsVisibleToPlayers`       | `bool`                                                 | `true`           |
| `PlayersCanCreateNPCs`        | `bool`                                                 | `false`          |

> **`SheetEditByOthers` note**: the host is always exempt — they can edit any sheet regardless of this setting.
>
> **`RollsVisibleToPlayers` note**: when `false`, each player's roll log only shows their own results. The host always sees every roll regardless of this setting. The display view (§13) also shows no rolls when `false`.
>
> **`PlayersCanCreateNPCs` note**: when `true`, the creating player's `userId` is stored as `OwnerUserId` on the new `NPCToken`. Movement follows the `TokenMovement` setting (e.g. under `OwnerOrHost`, the creator and host can both move it).

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

    public CombatState? ActiveCombat { get; private set; }         // v1.x — always null in v1 (see §9.5)

    // Tokens live on Map.Tokens — not duplicated here.

    // All mutators are private; engine calls Execute(...) helpers.
}
```

Engine methods (verbs):

- **Maps**: `CreateMapAsync`, `RenameMapAsync`, `DeleteMapAsync` (cascade: every `MapImage` on the map is deleted from `IPluginStorage` and the per-room byte total is decremented per §5.5; all tokens on the map are permanently removed from state; if it was the active map, `ActiveMapId` shifts to the next map in list order or `null` if none remain; clients show an empty canvas when `ActiveMapId` is null), `DuplicateMapAsync`, `ReorderMapsAsync` (writes `Map.ListOrder` from the host's drag-reorder gesture per §4.2), `SetActiveMapAsync` (triggers `SpawnPlayerTokenAsync` for any registered player who has no token on the newly active map — see §7.1), `UpdateGridAsync`.
- **Images**: `AddImageAsync` (called by upload endpoint after disk write), `UpdateImageTransformAsync`, `ReorderImageLayerAsync`, `RemoveImageAsync` (deletes the file from `IPluginStorage` and decrements the per-room byte total per §5.5).
- **Tokens**: `SpawnPlayerTokenAsync` (called at session start for all lobby players, on mid-session join, and on `SetActiveMapAsync` for any registered player who has no token on the newly active map; places token at `Map.DefaultSpawnPosition` or map center; reuses the player's existing session-scoped `CharacterSheet` if one already exists, otherwise creates one), `SpawnNpcTokenAsync`, `SpawnHostExtraTokenAsync`, `MoveTokenAsync`, `UpdateTokenAsync` (rename/recolor/icon), `RemoveTokenAsync`, `SetTokenHiddenAsync`, `ConvertAbandonedPlayerTokensAsync` (internal — invoked by the `PlayerUnregistered` handler per §2.3 to flip a departing player's `PlayerToken`s to `HostExtraToken` with `RepresentsUserId = oldUserId`).
- **Sheets**: `CreateSheetAsync`, `UpdateSheetAttributeAsync`, `UpdateSheetFreeFieldsAsync` (name/notes/hp — `Hp`/`MaxHp` are nullable; if unset the HP row is hidden in the sheet panel), `DeleteSheetAsync`, `ChangeSchemaAsync` (cascade: attributes whose name matches the new schema retain their value, type mismatch resets to default; attributes absent from the new schema are removed from all sheets).
- **Dice**: `RollAsync`.
- **Combat (v1.x — deferred, see §9.5)**: `StartInitiativeAsync` (host; creates CombatState in WaitingForRolls with selected NPC + all player combatants), `SubmitInitiativeRollAsync` (player; rolls d20 + DEX modifier, stores in CombatantEntry), `SetNpcInitiativeAsync` (host; manual entry or auto-roll for an NPC combatant), `ForceInitiativeRollAsync` (host; force-rolls for a player who hasn't rolled), `AdvanceTurnAsync` (host; next turn, wraps + increments RoundNumber at end of order), `AddCombatantAsync` (host; insert NPC mid-combat at initiative position), `RemoveCombatantAsync` (host; delete combatant, auto-advance if it was their turn), `EndCombatAsync` (host; clears ActiveCombat).
- **Settings**: `UpdateSettingsAsync`.
- **Lifecycle**: `EndSessionAsync`.

Each verb is a small `Execute` block guarded by a permission check up front. Test fixtures mirror Spardle's structure (one per verb under `Unit/Logic/Games/DndMapper/`).

---

## 13. UI Pages

```
host/KnockBox.DndMapper/Pages/
    DndMapperLobby.razor                  (already exists, scaffold)
    DndMapperRoom.razor                   (NEW — entry; switches on Phase)
    DndMapperPlayingPhase.razor           (NEW — main play canvas, control view)
        Components/
            MapCanvas.razor               (SVG: images + grid + tokens)
            TokenLayer.razor              (interactive, uses outfitItemDrag.js style interop; hidden tokens render as 50% opacity with an eye-slash overlay for the host only, and are not rendered at all for non-host players)
            HostMapSwitcher.razor         (host-only sidebar)
            HostImageInspector.razor      (host-only transform handles)
            CharacterSheetPanel.razor     (per-player; switches between own/others)
            DiceRollerModal.razor
            RollLogPanel.razor            (filters results per §9.3 visibility rules)
            PermissionsPanel.razor        (host-only; mirrors §11)
            InitiativeBanner.razor        (v1.x — see §9.5)
            HostInitiativePanel.razor     (v1.x — see §9.5)
    DndMapperDisplay.razor                (v1.x — screen-shareable display view; see deferral note below)
```

> **Scope**: the display view is deferred to v1.x — the section below is the v1.x design. v1 ships with the host control view + player view only.

`DndMapperDisplay.razor` will live at `/room/dnd-mapper/{ObfuscatedRoomCode}/display`. No special authorization is required — the obfuscated room code's randomized GUIDs make external discovery infeasible, and the view has no interaction affordances so there is no harm in open access. Any user who navigates to the URL may view the display (useful for casting to a TV or projector without an active KnockBox session).

> **Platform extension required (v1.x).** The standard `IGameSessionService` is per-circuit and tied to a registered user via `ISessionServiceProvider`. The display view has no registered user, so it cannot resolve `DndMapperGameState` through the normal path. v1.x will need a thin **read-only observer attach** — e.g. an `IGameSessionService.AttachObserverAsync(routeIdentifier, obfuscatedRoomCode)` extension that walks the existing room registry, returns the room's `AbstractGameState` directly, and records nothing in the per-user session cache. The observer subscribes to `state.StateChangedEventManager` and disposes the subscription on circuit close. This is a small platform addition, not a DnD-Mapper-only concern; spec it in the platform architecture doc when v1.x implementation begins.

The display view subscribes to that state and re-renders on every state change, but:
- Renders the active map identically to `MapCanvas.razor` (images + grid + visible tokens).
- **Does not render** hidden tokens at all (no ghost, no eye-slash — they simply don't exist in this view).
- **Does not render** any GM control panels (no map switcher, no token panel, no image inspector, no permissions panel).
- **Roll log**: shown when `Settings.RollsVisibleToPlayers = true` (all rolls); hidden entirely when `false` (the display has no player identity, so it cannot show "own rolls only").
- Character sheets are visible read-only (same visibility rules as a player — names + attributes public, notes + HP hidden).
- No dice roller; no editing affordances of any kind.

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

**Net new infrastructure (platform)**: `IGameEngineHttpHandler` contract in `KnockBox.Core` SDK + a generic plugin-route dispatcher (`/api/plugins/{routeIdentifier}/{**path}`) in `KnockBox.Platform`. Drives both the image upload (§5.1) and image serve (§5.4) endpoints without leaking plugin-specific names into `Program.cs`.

**Net new infrastructure (plugin)**: square-grid SVG component, token drag interop (adapt `outfitItemDrag.js`), image transform handles, dice modal + roll log component, attribute-schema editor, map switcher.

---

## 15. Out of MVP Scope (Explicit)

These are intentionally excluded from v1 to keep the design scoped. Any of them can be revisited as v1.x increments.

- **Conditions and status effects** on combatants (poisoned, stunned, etc.) — manual narration only in v1.
- **Fog of war / vision masking** — host-controlled visibility regions.
- **Hex grids**.
- **Measurement tools** (ruler, AoE templates, line-of-sight).
- **Token uploaded artwork** — v1 uses initial-letter or solid-disc icons (O-4).
- **Map cross-session persistence** — every session starts empty in v1. Save/reload of campaigns is planned as a future feature.
- **Real-time drag previews** — only final drop is broadcast.
- **Audio / video chat / soundboard**.
- **Multi-host / co-GM**.
- **Macros and rollable tables**.
- **Import/export of campaigns** (no save format).
- **Mobile-optimized layout** — desktop-first.
- **Host "center viewport for everyone" broadcast** — the §6.4 affordance that nudges all clients' viewports to a specific cell. Deferred to v1.x.
- **Initiative / turn-order tracker** — the full §9.5 design (combat state, banner, host panel, force-rolls, mid-combat add/remove). Deferred to v1.x.
- **Screen-shareable display view** — the full §13 `DndMapperDisplay.razor` page plus the platform observer-attach extension. Deferred to v1.x.

---

## 16. Open Questions (Iterate with User)

| ID  | Question                                                                                                                                                  | Current default                |
| --- | --------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------ |
| O-1 | Tokens per-map vs session-global with last-position memory? **Closed.** | Per-map. |
| O-2 | Can the host upload images / author maps during the lobby phase? **Closed.** | Lobby authoring allowed. |
| O-3 | Player override of grid-line visibility — local UI toggle, or host-controlled? **Closed.** | Local UI toggle per player. |
| O-4 | Token icons — letter/solid-disc only, or also per-token uploaded portraits? **Closed.** | Letter / solid disc only in v1. |
| O-5 | Cross-session persistence — save and reload maps across sessions? **Closed.** | Ephemeral in v1; save/reload planned as a future feature. |
| O-6 | Token-move animation — tween from old to new position client-side? **Closed.** | Yes, ~150 ms tween. |
| O-7 | Sheet HP and Notes visibility. **Closed.** | Visible to owner and host only. |
| O-8 | Per-room and per-image storage caps? **Closed.** | 5 MB / image, 10 MB / room (hardcoded). |
| O-9 | Should players see each other's roll results? **Closed.** | Controlled by `RollsVisibleToPlayers` session setting (default `true`). Host always sees all rolls. |
| O-10| Hidden tokens — should the host see a ghost so they don't forget? **Closed.** | Yes, ghosted with eye-slash overlay in the control view. |

---

## 17. Verification (when implementation begins)

When the implementation phase starts, the following validation matrix should be run:

1. **Build**: `dotnet build host/KnockBox.Host.slnx` succeeds; `KnockBox.DndMapper.dll` is staged into `host/KnockBox/bin/{Config}/{TFM}/games/KnockBox.DndMapper/`.
2. **Unit tests**: `dotnet test host/KnockBox.DndMapperTests/KnockBox.DndMapperTests.csproj` passes for every engine verb — happy path + permission rejection + invalid-input path.
3. **End-to-end (v1)**: with the host running, two browsers (host + one player) join the same room. Verify:
   - Host uploads an image; both clients see it on the active map.
   - Host transforms the image; both clients see the new position/scale.
   - Host creates a second map; switcher updates; switching active map propagates to player.
   - Player drags own token; host sees move within the SignalR latency budget.
   - Player tries to drag host's NPC token under default permissions — blocked.
   - Player rolls 1d20 + DEX modifier; result appears in shared log (all clients).
   - Host sets `RollsVisibleToPlayers = false`; player rolls again — result visible to roller and host only, not to other players.
   - Host changes `TokenMovement` to `Anyone`; player can now move NPC.
   - Host clicks "End Session"; both clients return home; uploaded image files are removed from disk.
4. **Initiative tracker (v1.x — deferred)**: out of scope for v1; see §9.5. The v1.x verification matrix will cover: host starts initiative and selects two NPC tokens; both players and both NPC combatants appear in WaitingForRolls banner; one player rolls, the other doesn't; host force-rolls for the second player; host enters initiative for both NPCs; banner automatically transitions to Active phase showing sorted turn order with round 1; host clicks "Next Turn" through the full order, with the banner wrapping to round 2 on the last combatant; host removes one NPC mid-combat (if it was their turn, next combatant is highlighted); host ends combat and the banner disappears on all clients.
5. **Display view (v1.x — deferred)**: out of scope for v1; see §13. The v1.x verification matrix will cover: any browser (including unauthenticated) opens the `/display` URL and confirms hidden tokens are absent, GM panels are absent, and the roll log is empty while `RollsVisibleToPlayers = false`; host sets `RollsVisibleToPlayers = true` and the display tab now shows the roll log.
6. **Architecture invariant**: `KnockBox.csproj` still has zero `using` of `KnockBox.DndMapper` types; `ReferenceOutputAssembly="false"` on the project ref is preserved. The new `IGameEngineHttpHandler` contract (§5.1) lives in `KnockBox.Core` SDK and is opted into by `DndMapperGameEngine`; no plugin-specific names appear in `Program.cs`.

---

*End of v1 draft. Ready for iteration — pick any section to refine or flip any open-question default and the doc will be updated in place.*
