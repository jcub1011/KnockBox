# M01 — State Model & Engine Verbs

> **Goal**: replace the empty DnD Mapper scaffold with the full v1 state model and every engine verb except image management. After M01, the engine layer can drive an entire session through unit tests; no UI changes yet.
>
> **Dependencies**: existing scaffold only. No prior milestones required. **No SDK changes required** — the platform's `RegisterPlayer` already rejects new players once a session has started (`IsJoinable == false`), so mid-session join is structurally impossible and no `PlayerRegistered` event is needed. Initial player tokens are spawned in `StartAsyncCore`; the only post-Start auto-spawn path is `SetActiveMapAsync` for players already registered before Start.
>
> **GDD references**: §2.3 (disconnect handling), §3 (lifecycle), §4 (maps), §6 (grid), §7 (tokens), §8 (sheets), §9 (dice — excluding §9.5 initiative which is v1.x), §11 (permissions), §12 (state model sketch — engine verbs list, minus image and combat verbs).
>
> **Out of scope** (do NOT implement here): `AddImageAsync`, `UpdateImageTransformAsync`, `ReorderImageLayerAsync`, `RemoveImageAsync` (M03). Plugin HTTP dispatcher (M02). Razor pages beyond the existing scaffold (M04, M05). Lobby authoring UI (M06). Combat / initiative tracker, display view, observer-attach (all v1.x — `ActiveCombat`, `CombatState`, `CombatantEntry`, `InitiativeBanner`, `HostInitiativePanel` MUST NOT appear in v1 code).

---

## 1. Context

The DnD Mapper plugin scaffold at `host/KnockBox.DndMapper/` currently has an empty state class, a no-op engine, and a basic lobby that flips between "lobby UI" and a "Game scaffold — gameplay coming soon." placeholder. The GDD (`host/KnockBox.DndMapper/Specs/Plans/dnd-mapper-gdd.md`) defines what fills it.

This milestone implements the **engine layer** end-to-end: every record, enum, state field, and verb method needed to play the game programmatically. Once M01 is merged, all subsequent milestones (M02–M05) layer the HTTP transport, image storage, and Razor UI on top of a stable engine. M06 adds polish + verification.

The platform's mutation contract is non-negotiable: **all state mutations must go through `state.Execute*` / `state.ExecuteAsync*`**. Engine verbs are the only public mutation surface; state setters are private. Notifications fire *outside* the lock (so subscribers — including the engine's own `PlayerUnregistered` handler — can re-enter `Execute` without deadlock).

---

## 2. Files to create / modify

### New files (under `host/KnockBox.DndMapper/`)

Create these as plain C# classes / records / enums. Project structure follows the existing `Services/State/Games/` and `Models/` conventions used by other plugins.

```
Models/
    DndMapperPhase.cs
    TokenType.cs
    TokenIconKind.cs
    AttributePreset.cs
    AttributeValueType.cs
    TokenMovementPolicy.cs
    SheetEditPolicy.cs
    RollMode.cs
Services/State/Games/Data/
    DndMapperSettings.cs
    AttributeSchema.cs
    AttributeRow.cs
    AttributeValue.cs
    GridConfig.cs
    Map.cs
    MapImage.cs
    Token.cs
    CharacterSheet.cs
    RollResult.cs
    RollRequest.cs
    DiceTerm.cs
    AttributeRef.cs
Services/Logic/Games/
    DefaultColorPalette.cs            ; static helper — see §6
```

### Files to modify

- `host/KnockBox.DndMapper/Services/State/Games/DndMapperGameState.cs` — replace the 12-line scaffold with the full GDD §12 shape. Constructor seeds defaults.
- `host/KnockBox.DndMapper/Services/Logic/Games/DndMapperGameEngine.cs` — extend the 43-line scaffold: inject `IRandomNumberService`, subscribe `state.PlayerUnregistered` in `CreateStateAsync`, override `StartAsyncCore` to flip phase + spawn player tokens, add every verb listed in §3.6.
- `host/KnockBox.DndMapperTests/` — add the test classes listed in §7.

### Files NOT touched in M01

- `Pages/DndMapperLobby.razor` (and `.razor.cs`, `.razor.css`) — left alone; M04 demotes it from a `@page` to a regular component.
- `Pages/Components/DndMapperHeader.razor`, `DndMapperTile.razor` — untouched.
- `plugin.json` — untouched. (M03 adds the `Storage` capability.)
- `KnockBox.DndMapper.csproj` — untouched.

---

## 3. Detailed work breakdown

### 3.1 Models (enums)

Create the following enums with no logic; just discriminator values. All are simple `public enum` types in `KnockBox.DndMapper.Models`.

- `DndMapperPhase { Lobby, Playing }`
- `TokenType { PlayerToken, NPCToken, HostExtraToken }`
- `TokenIconKind { Initial, Solid }`
- `AttributePreset { DnD5eCore, DnD5ePlusCommonSkills, SimpleD20, Custom }`
- `AttributeValueType { Score, Modifier, Text }`
- `TokenMovementPolicy { OwnerOrHost, Anyone, HostOnly }`
- `SheetEditPolicy { OwnersOnly, OwnersAndHost, Anyone }`
- `RollMode { Normal, Advantage, Disadvantage }`

### 3.2 Records / data classes (in `Services/State/Games/Data/`)

Use C# records (or readonly record structs for small value types) where the data is pure-value and has no identity beyond its fields. Use classes when the type is mutated in place by the engine (e.g. `Map`, `Token`, `CharacterSheet` — these need stable identity for dictionary lookups and engine in-place updates inside `Execute`).

- `DndMapperSettings` — class with mutable properties:
  - `TokenMovement : TokenMovementPolicy` (default `OwnerOrHost`)
  - `SheetEditByOthers : SheetEditPolicy` (default `OwnersAndHost`)
  - `RollsVisibleToPlayers : bool` (default `true`)
  - `PlayersCanCreateNPCs : bool` (default `false`)
  - Provides a `Clone()` method (used by `UpdateSettingsAsync` for value-semantics broadcast).
- `AttributeSchema` — class:
  - `Preset : AttributePreset`
  - `Rows : IReadOnlyList<AttributeRow>` (derived from preset, except for `Custom`).
  - Static factory: `AttributeSchema.FromPreset(AttributePreset)` returning the canonical row list for each non-Custom preset.
  - DnD5eCore rows: STR, DEX, CON, INT, WIS, CHA — all `Score` type with default `10`.
  - DnD5ePlusCommonSkills: the six abilities (Score, default 10) + Athletics, Stealth, Perception, Persuasion, Investigation (Modifier, default 0).
  - SimpleD20: a single `Modifier` row named `"Modifier"`, default 0.
- `AttributeRow(string Name, AttributeValueType Type, AttributeValue Default)` — record. `Default` is the seed value for new sheets.
- `AttributeValue` — discriminated union via positional record + factory:
  - `static AttributeValue Score(int score)` / `static AttributeValue Modifier(int modifier)` / `static AttributeValue Text(string text)`.
  - Properties: `Type : AttributeValueType`, `IntValue : int?`, `StringValue : string?`.
  - Helper: `int? GetModifier()` — for `Score` returns `(IntValue.Value - 10) / 2` (D&D 5e ability-modifier formula, integer truncation, e.g. score 9 → −1, score 10 → 0); for `Modifier` returns `IntValue`; for `Text` returns `null`.
- `GridConfig` — class with mutable properties (host edits these per-map):
  - `WidthCells : int` (default 30, validated 5–200 by `UpdateGridAsync`)
  - `HeightCells : int` (default 20, validated 5–200)
  - `CellPixels : int` (default 50)
  - `ShowGridLines : bool` (default `true`)
  - `SnapToGrid : bool` (default `true`)
  - `LineColor : string` (default `"#222"`)
  - `Clone()` method.
- `Map` — class:
  - `Id : Guid`, `Name : string`, `Grid : GridConfig`, `Images : List<MapImage>`, `Tokens : List<Token>`, `CreatedUtc : DateTime`, `ListOrder : int`, `DefaultSpawnPosition : (double X, double Y)?`.
  - Constructor seeds `Grid = new GridConfig()`, empty lists.
- `MapImage` — class with mutable transform fields. Defined in M01 even though no verb in M01 mutates it — M03's verbs need the type.
  - `Id : Guid`, `RelativePath : string`, `X : double`, `Y : double`, `Width : double`, `Height : double`, `Rotation : double`, `Opacity : double` (default 1.0), `LayerOrder : int`, `Locked : bool`, `ByteSize : long` (set on upload — M03 populates).
- `Token` — class with mutable position + display fields (engine mutates in place inside `Execute`):
  - `Id : Guid`, `Type : TokenType`, `OwnerUserId : string?`, `RepresentsUserId : string?`, `Name : string`, `Color : string`, `IconKind : TokenIconKind`, `MapId : Guid`, `X : double`, `Y : double`, `SheetId : Guid?`, `Hidden : bool` (default `false`).
- `CharacterSheet` — class:
  - `Id : Guid`, `OwnerUserId : string?`, `CharacterName : string`, `Values : Dictionary<string, AttributeValue>`, `Notes : string` (default `""`), `Hp : int?` (nullable — unset hides the HP row), `MaxHp : int?`.
- `RollResult` — record:
  - `Id : Guid`, `RollerUserId : string`, `ForcedByUserId : string?` (always `null` in v1 — reserved for v1.x initiative-tracker force-roll), `Rolls : IReadOnlyList<DieRoll>`, `Total : int`, `Mode : RollMode`, `FlatModifier : int`, `AttributeModifier : int?`, `Label : string`, `TimestampUtc : DateTime`.
  - `DieRoll(int Sides, int Result, bool Discarded)` — record. `Discarded = true` for the d20 that lost in adv/dis (so the UI can render it struck-through).
- `RollRequest` — record:
  - `Dice : IReadOnlyList<DiceTerm>`, `AttributeRef : AttributeRef?`, `FlatModifier : int`, `Mode : RollMode`, `Label : string`.
- `DiceTerm(int Count, int Sides)` — record. Sides ∈ {4, 6, 8, 10, 12, 20, 100}; count ≥ 1. Sum of all `Count` across dice in a request must be ≤ 20 (validated in `RollAsync`).
- `AttributeRef(Guid SheetId, string AttributeName)` — record.

### 3.3 `DefaultColorPalette` static helper

`Services/Logic/Games/DefaultColorPalette.cs`. Eight high-contrast, color-blind-friendly hex colors:

```csharp
internal static class DefaultColorPalette
{
    private static readonly string[] _palette =
    [
        "#1f77b4", "#ff7f0e", "#2ca02c", "#d62728",
        "#9467bd", "#8c564b", "#e377c2", "#17becf"
    ];

    public const string Neutral = "#888";

    public static string ForPlayerSlot(int slotIndex)
        => _palette[((slotIndex % _palette.Length) + _palette.Length) % _palette.Length];
}
```

The slot index for a player is the zero-based index of `User.Id` in `state.Players` at the moment the player's first token is created. Players that join, leave, and rejoin get a **fresh** slot index per the GDD §2.3 rejoin semantics (rejoin = mid-session join, fresh token + fresh sheet).

### 3.4 `DndMapperGameState`

Replace the existing class entirely:

```csharp
public sealed class DndMapperGameState : AbstractGameState
{
    public DndMapperPhase Phase { get; private set; } = DndMapperPhase.Lobby;
    public DndMapperSettings Settings { get; private set; } = new();
    public AttributeSchema AttributeSchema { get; private set; }
        = AttributeSchema.FromPreset(AttributePreset.DnD5eCore);

    public List<Map> Maps { get; } = [];
    public Guid? ActiveMapId { get; private set; }

    public Dictionary<Guid, CharacterSheet> Sheets { get; } = [];
    public List<RollResult> RollLog { get; } = [];

    public const int RollLogCap = 200;

    public DndMapperGameState(User host, ILogger<DndMapperGameState> logger) : base(host, logger)
    {
        SetJoinable(true);
    }

    // ── Internal mutators (only callable from inside Execute via the engine) ──
    // Prefer adding small `internal void Apply...` helpers here rather than
    // exposing setters publicly; engine verbs invoke them inside Execute blocks.

    internal void SetPhase(DndMapperPhase phase) => Phase = phase;
    internal void SetSettings(DndMapperSettings settings) => Settings = settings;
    internal void SetAttributeSchema(AttributeSchema schema) => AttributeSchema = schema;
    internal void SetActiveMapId(Guid? mapId) => ActiveMapId = mapId;

    internal void AppendRoll(RollResult result)
    {
        RollLog.Add(result);
        if (RollLog.Count > RollLogCap)
            RollLog.RemoveRange(0, RollLog.Count - RollLogCap);
    }
}
```

The internal mutator helpers exist so the engine doesn't manipulate properties directly but still keeps state setters out of the public surface.

### 3.5 `DndMapperGameEngine`

Replace the existing scaffold. The engine is a singleton (`AddGameEngine<>` already registers it that way); no per-room state lives on the engine itself — everything is on `DndMapperGameState`.

```csharp
public sealed class DndMapperGameEngine : AbstractGameEngine
{
    private readonly ILogger<DndMapperGameEngine> _logger;
    private readonly ILogger<DndMapperGameState> _stateLogger;
    private readonly IRandomNumberService _rng;

    public DndMapperGameEngine(
        ILogger<DndMapperGameEngine> logger,
        ILogger<DndMapperGameState> stateLogger,
        IRandomNumberService rng)
    {
        _logger = logger;
        _stateLogger = stateLogger;
        _rng = rng;
    }

    public override int MinPlayerCount => 1;     // GM + zero-or-more players (host doesn't count toward player count)
    public override int MaxPlayerCount => 8;     // GDD §1 says ~6 + host; allow a little headroom

    public override Task<ValueResult<AbstractGameState>> CreateStateAsync(
        User host, CancellationToken ct = default)
    {
        if (host is null)
            return Task.FromResult(ValueResult<AbstractGameState>.FromError("Host is required."));

        var state = new DndMapperGameState(host, _stateLogger);
        state.PlayerUnregistered += player => HandlePlayerLeft(player, state);
        return Task.FromResult<ValueResult<AbstractGameState>>(state);
    }

    protected override Task<Result> StartAsyncCore(
        AbstractGameState abstractState, CancellationToken ct = default)
    {
        if (abstractState is not DndMapperGameState state)
            return Task.FromResult(Result.FromError("State type mismatch."));

        var executeResult = state.Execute(() =>
        {
            state.SetPhase(DndMapperPhase.Playing);
            state.SetJoinable(false);

            // If the host pre-authored maps in the lobby, ActiveMapId is set.
            // Otherwise pick the first map in list-order, or stay null (empty canvas allowed).
            if (state.ActiveMapId is null && state.Maps.Count > 0)
                state.SetActiveMapId(state.Maps.OrderBy(m => m.ListOrder).First().Id);

            // Spawn player tokens for every registered player on the active map.
            if (state.ActiveMapId is Guid mapId
                && state.Maps.FirstOrDefault(m => m.Id == mapId) is Map activeMap)
            {
                int slot = 0;
                foreach (var entry in state.Players)
                {
                    SpawnPlayerTokenInternal(state, activeMap, entry.User, slot++);
                }
            }
        });

        return Task.FromResult(executeResult);
    }

    // (verb methods follow — see §3.6)

    private void HandlePlayerLeft(User player, DndMapperGameState state)
    {
        // PlayerUnregistered fires OUTSIDE the lock, so re-entering Execute is safe.
        // Convert all PlayerTokens owned by `player` into HostExtraTokens.
        state.Execute(() => ConvertAbandonedPlayerTokensInternal(state, player));
    }
}
```

> **Engine notes**
> - `MinPlayerCount = 1` so a single-player party (just the GM rolling test dice solo) is allowed if desired. Adjust to `0` if `LobbyService` blocks zero-player Start.
> - `MaxPlayerCount = 8` keeps slot-color cycling clean (palette is 8). The GDD says "up to ~6"; 8 leaves headroom without wrapping color slots.
> - **Override return type matches `AbstractGameEngine`**: `Task<ValueResult<AbstractGameState>>` for `CreateStateAsync`, `Task<Result>` for `StartAsyncCore` (verified against the existing scaffold and `sdk/KnockBox.Core/.../AbstractGameEngine.cs`). Do not switch to `ValueTask<...>`.
> - **`Execute(Action)` returns `Result` directly**; do not use `Execute<TReturn>(Func<TReturn>)` unless you actually want a `ValueResult<TReturn>` wrapper. All verb pseudocode in §3.6 follows the Action form: mutate inside the lambda, then return the outer `Result.Success` (or capture failure via local variable + return after `Execute` returns success).
> - The engine subscribes to `PlayerUnregistered` once at state creation. The event fires outside the execute lock — re-entering `Execute` from the handler is safe and required. (There is no symmetric `PlayerRegistered` subscription: the platform refuses new player registrations once `IsJoinable == false`, which `StartAsyncCore` flips, so post-Start joins cannot happen.)

### 3.6 Engine verbs

Each verb follows the same pattern:

```csharp
public Result Verb(DndMapperGameState state, User caller, /* args */)
{
    // 1. Up-front argument null/range checks (return Result.FromError on failure).
    // 2. Permission check (return Result.FromError on rejection).
    // 3. var executeResult = state.Execute(() => { mutate-in-place; }); // Action form, returns Result.
    // 4. return executeResult; (already a Result; failure surfaces from Execute itself)
    //
    //    For verbs that capture an output (e.g. CreateMapAsync needs the new Guid):
    //    Guid newId = default;
    //    var executeResult = state.Execute(() => { newId = Guid.NewGuid(); state.Maps.Add(...); });
    //    if (executeResult.IsFailure) return ValueResult<Guid>.FromError(executeResult.Error);
    //    return newId;
}
```

Where the verb returns a value (e.g. `CreateMapAsync` returns the new `Map.Id`), use `ValueResult<T>` and capture the new value via a local set inside the `Execute(Action)` lambda (per the comment block above). Do NOT use the `Execute<TReturn>(Func<TReturn>)` overload to "return" the value — that overload returns `ValueResult<TReturn>` and double-wraps `Result` types awkwardly. M01 verbs are synchronous; expose them as plain `Result` / `ValueResult<T>` and add `Async` suffix only if the body genuinely awaits. The GDD §12 list uses `Async` suffixes for consistency — match the GDD names so the planner-implementer mapping is unambiguous, but the return types stay synchronous.

#### Map verbs

| Verb | Caller permission | Effects |
|------|------------------|---------|
| `CreateMapAsync(state, caller, name) → ValueResult<Guid>` | host only | Append `Map` to `state.Maps` with `Id = Guid.NewGuid()`, `ListOrder = state.Maps.Count`, `CreatedUtc = DateTime.UtcNow`, default `GridConfig`. Returns the new map's id. |
| `RenameMapAsync(state, caller, mapId, newName) → Result` | host only | Reject if `newName` is null/whitespace or > 60 chars. |
| `DeleteMapAsync(state, caller, mapId) → Result` | host only | **In-memory cascade only in M01** — remove the map from `state.Maps`. If `state.ActiveMapId == mapId`, set `ActiveMapId` to the next map by `ListOrder` ascending, or `null` if no maps remain. **M03 will extend this** to also delete each `MapImage` file via `IPluginStorage`. |
| `DuplicateMapAsync(state, caller, mapId) → ValueResult<Guid>` | host only | Deep-clone the map (including grid, but NOT images — images stay referenced from the source map; M03 will revisit if duplicate-images-via-storage-copy is needed). For tokens: do NOT clone tokens; the duplicate map starts with empty `Tokens`. New `Id`, `Name = "{old} (copy)"`, `ListOrder = state.Maps.Count`, `CreatedUtc = DateTime.UtcNow`. |
| `ReorderMapsAsync(state, caller, IReadOnlyList<Guid> orderedIds) → Result` | host only | Validate `orderedIds` is a permutation of `state.Maps.Select(m => m.Id)` — reject if missing/extra ids. Assign each map's `ListOrder` from its position in the list. |
| `SetActiveMapAsync(state, caller, mapId) → Result` | host only | Set `ActiveMapId = mapId`. For every registered player whose `User.Id` lacks a token on the new map, call `SpawnPlayerTokenInternal` to place one at the map's `DefaultSpawnPosition` (or map center if null). |
| `UpdateGridAsync(state, caller, mapId, GridConfig newGrid) → Result` | host only | Validate `5 ≤ WidthCells ≤ 200`, `5 ≤ HeightCells ≤ 200`, `CellPixels ≥ 1`. Replace `Map.Grid` with `newGrid.Clone()`. |

> **Spawn placement rule (used by `SetActiveMapAsync` and `StartAsyncCore`)**: place at `Map.DefaultSpawnPosition` if non-null; otherwise at `(WidthCells / 2.0, HeightCells / 2.0)`.

#### Token verbs

| Verb | Caller permission | Effects |
|------|------------------|---------|
| `SpawnPlayerTokenAsync(state, caller, userId) → ValueResult<Guid>` | host (admin spawn) or self (`caller.Id == userId`) | Spawns a `PlayerToken` for `userId` on the active map at default-spawn position. Reuses an existing player sheet from `state.Sheets` (where `OwnerUserId == userId`) or creates one with `AttributeSchema`-default values. Returns the new token's id. **Internal helper** `SpawnPlayerTokenInternal(state, Map activeMap, User player, int slot)` is what the engine calls during `StartAsyncCore` and `SetActiveMapAsync`. |
| `SpawnNpcTokenAsync(state, caller, mapId, name) → ValueResult<Guid>` | host always; player iff `state.Settings.PlayersCanCreateNPCs == true` | Creates an `NPCToken` on `mapId` **at the map's center** (`(Grid.WidthCells / 2.0, Grid.HeightCells / 2.0)`) per GDD §7.4. `OwnerUserId = caller.Id` only when the caller is a non-host player and the setting allows it; otherwise `OwnerUserId = null`. `Color = DefaultColorPalette.Neutral`. `IconKind = Initial`. |
| `SpawnHostExtraTokenAsync(state, caller, mapId, name, representsUserId) → ValueResult<Guid>` | host only | `Type = HostExtraToken`. Spawned **at the map's center** per GDD §7.4. `RepresentsUserId` may be null; if set, color comes from that player's slot; if null, `Color = Neutral`. `OwnerUserId = null` always. |
| `MoveTokenAsync(state, caller, tokenId, newX, newY) → Result` | per `state.Settings.TokenMovement` (see policy table below) | Update `Token.X`, `Token.Y`. Don't snap server-side; clients snap on drag. Validate coordinates lie within the inclusive map bounds: `0 ≤ newX ≤ Map.Grid.WidthCells` and `0 ≤ newY ≤ Map.Grid.HeightCells`. (Inclusive at the upper bound so a token may sit exactly on the rightmost/bottommost wall — useful for "off-board" staging at the edge. Tokens at *fractional* cell coordinates inside `[0, WidthCells]` are valid; the renderer multiplies by `CellPixels` per §6.3.) |
| `UpdateTokenAsync(state, caller, tokenId, name, color, iconKind) → Result` | host always; `caller.Id == token.OwnerUserId` for player-owned tokens | Mutates display fields. Reject empty/whitespace `name`. |
| `RemoveTokenAsync(state, caller, tokenId) → Result` | host always; for `NPCToken` created under `PlayersCanCreateNPCs`, `caller.Id == token.OwnerUserId` is also allowed. **`PlayerToken`s are never removable via this verb** — they are auto-managed (created by `SpawnPlayerTokenInternal`, transformed by `ConvertAbandonedPlayerTokensInternal` on disconnect). Reject any attempt to remove a `PlayerToken`, even by the host. | Removes the token from its map. |
| `SetTokenHiddenAsync(state, caller, tokenId, hidden) → Result` | host only | Flip `Token.Hidden`. |
| `ConvertAbandonedPlayerTokensAsync(state, departingUserId) → Result` | **internal** — not exposed publicly. Invoked from the `PlayerUnregistered` handler. | For every `PlayerToken` with `OwnerUserId == departingUserId`: set `Type = HostExtraToken`, `OwnerUserId = null`, `RepresentsUserId = departingUserId`. Position, sheet ref, name, color preserved. Sheet itself is NOT deleted. |

> **`MoveTokenAsync` permission table (per §7.3 step 3)**:
> - `TokenMovement = OwnerOrHost` → `caller.Id == token.OwnerUserId` OR `caller.Id == state.Host.Id`.
> - `TokenMovement = Anyone` → any registered participant (host or player) may move any token.
> - `TokenMovement = HostOnly` → only host. (Players can't even move their own.)

> **Sheet auto-creation in `SpawnPlayerTokenInternal`**: if `state.Sheets` already contains a sheet with `OwnerUserId == player.Id`, reuse that `SheetId` on the new token. Otherwise create one with `CharacterName = player.Name`, `Values` seeded from `state.AttributeSchema.Rows[i].Default`, `Notes = ""`, `Hp = null`, `MaxHp = null`. This makes "one player → one session-scoped sheet, multiple per-map tokens" work automatically (per §7.1 / §8.4).

#### Sheet verbs

| Verb | Caller permission | Effects |
|------|------------------|---------|
| `CreateSheetAsync(state, caller, ownerUserId, characterName) → ValueResult<Guid>` | host only (host pilots NPC sheets via this; player sheets are created by `SpawnPlayerTokenInternal`) | New `CharacterSheet` with `Id = Guid.NewGuid()`, `OwnerUserId = ownerUserId`, `Values` seeded from `state.AttributeSchema`. |
| `UpdateSheetAttributeAsync(state, caller, sheetId, attributeName, AttributeValue value) → Result` | per `state.Settings.SheetEditByOthers` (see table) | Set `Values[attributeName] = value`. Reject if `attributeName` is not in `state.AttributeSchema.Rows`. Reject if `value.Type` doesn't match the row's `Type`. |
| `UpdateSheetFreeFieldsAsync(state, caller, sheetId, characterName, notes, hp, maxHp) → Result` | per `state.Settings.SheetEditByOthers` (host always allowed) | All four fields updateable (nullable Hp / MaxHp accepted as null). Reject empty `characterName`. |
| `DeleteSheetAsync(state, caller, sheetId) → Result` | host only | Remove from `state.Sheets`. Iterate every map's tokens; for any token with `SheetId == sheetId`, set `SheetId = null`. |
| `ChangeSchemaAsync(state, caller, AttributeSchema newSchema) → Result` | host only | Replace `state.AttributeSchema = newSchema`. For every sheet in `state.Sheets`: rebuild `Values` — for each row in `newSchema.Rows`, copy over the old value if both the name AND `AttributeValueType` match; otherwise reset to the row's `Default`. Drop any old keys not present in the new schema. |

> **`SheetEditByOthers` permission table**:
> - `OwnersOnly` → `caller.Id == sheet.OwnerUserId` OR `caller.Id == state.Host.Id` (host always exempt).
> - `OwnersAndHost` → same as `OwnersOnly` (host always exempt — kept distinct from `OwnersOnly` so the UI can render the toggle clearly; behaviorally identical for v1).
> - `Anyone` → any participant.

> **Why `OwnersOnly` and `OwnersAndHost` are behaviorally identical**: the host is always exempt from `SheetEditByOthers`. The two settings differ in *intent* (does the host's editing get UI-emphasized?) more than runtime behavior. This is the GDD §11 spec.

#### Settings verb

| Verb | Caller permission | Effects |
|------|------------------|---------|
| `UpdateSettingsAsync(state, caller, DndMapperSettings newSettings) → Result` | host only | Replace `state.Settings = newSettings.Clone()`. Mid-session live edits allowed in any phase. |

#### Dice verb

`RollAsync(state, caller, RollRequest request) → ValueResult<RollResult>`:

1. Validate `request.Dice` is non-empty and `request.Dice.Sum(d => d.Count) ≤ 20` — reject `"Cannot roll more than 20 dice in a single request."`.
2. Validate every `DiceTerm.Sides` is in `{4, 6, 8, 10, 12, 20, 100}` and `Count ≥ 1` — reject otherwise.
3. If `request.Mode != Normal`: validate `request.Dice` contains exactly one entry equal to `(Count: 1, Sides: 20)`. Reject `"Advantage/Disadvantage requires exactly one d20."` otherwise.
4. If `request.AttributeRef` is set: validate the sheet exists and the attribute name is in the sheet. If the sheet's `OwnerUserId` is not the caller and the caller is not the host, reject `"You may only reference your own attributes (or be host)."`. Resolve the modifier via `AttributeValue.GetModifier()`; reject if the value type returns no modifier (e.g. `Text`). `attributeModifier` is the resolved int; null if no `AttributeRef`.
5. Roll dice via `_rng.GetRandomInt(1, sides + 1, RandomType.Fast)`, one call per die. Build a `List<DieRoll>` recording each (sides, result, discarded=false).
6. Apply adv/dis: in `Advantage` mode, find the two d20 rolls (you must roll a *second* d20 to compute the higher) — actually the precondition guarantees a single `(1, 20)` term, so step 5 produced exactly one d20 roll. Roll a second d20 here, append it to the list. Mark whichever is lower (Advantage) or higher (Disadvantage) as `Discarded = true`. Use the kept value for the total.

   > Implementation note: roll the second d20 in step 6 only (don't pre-add to dice; `request.Dice` in the wire shape is what the user requested, and step 5 honors it literally — modes are applied as a post-step). The `RollResult.Rolls` list ends up with both d20s, one marked discarded.
7. Compute `Total`: sum of non-discarded rolls + `request.FlatModifier` + `attributeModifier ?? 0`.
8. Build the `RollResult`:
   - `Id = Guid.NewGuid()`
   - `RollerUserId = caller.Id`
   - `ForcedByUserId = null` (always — v1.x reserved)
   - `Rolls`, `Total`, `Mode`, `FlatModifier`, `AttributeModifier`, `Label = request.Label ?? ""`
   - `TimestampUtc = DateTime.UtcNow`
9. Inside `state.Execute(() => state.AppendRoll(result))` — append-and-cap. The cap (200) drops oldest rolls when overflowing.
10. Return `ValueResult<RollResult>.FromSuccess(result)`.

#### Lifecycle verb

| Verb | Caller permission | Effects |
|------|------------------|---------|
| `EndSessionAsync(state, caller) → Result` | host only | In M01: just calls `state.Dispose()` (which fires `OnStateDisposed`, propagates through the platform's session lifecycle, redirecting clients home). **M03 will extend this** to enumerate per-room image files and call `IPluginStorage.Delete` on each before disposing. |

### 3.7 Wire `IRandomNumberService` into module DI

`DndMapperModule.cs` currently does:

```csharp
public void RegisterServices(IPluginRegistration registration)
    => registration.AddGameEngine<DndMapperGameEngine>();
```

`IRandomNumberService` is registered by the host at the platform level, so plugins can resolve it via the DI container without explicit registration. **No `DndMapperModule` changes required for M01.** `AddGameEngine<DndMapperGameEngine>()` registers the engine via `Create<TEngine>(sp)` which uses `sp` (the plugin's scoped service provider) for ctor parameter resolution; `IRandomNumberService` injects automatically.

> Verify in implementation: confirm `IPluginRegistration`'s engine instantiation does walk the parent service provider for non-plugin-registered ctor params (it should — `IRandomNumberService` is the same path DiceSimulator's engine uses).

---

## 4. Acceptance criteria

- [ ] `dotnet build host/KnockBox.Host.slnx` succeeds with no warnings introduced by this milestone.
- [ ] `KnockBox.DndMapper.dll` continues to stage into `host/KnockBox/bin/{Config}/{TFM}/games/KnockBox.DndMapper/`.
- [ ] `dotnet test host/KnockBox.DndMapperTests/KnockBox.DndMapperTests.csproj` is green, with the test classes / methods listed in §7 present and passing.
- [ ] `dotnet test sdk/KnockBox.Sdk.slnx` is green (no SDK changes in this milestone).
- [ ] `KnockBox.csproj` still has zero `using KnockBox.DndMapper.*` and the `<ProjectReference>` to `KnockBox.DndMapper` retains `ReferenceOutputAssembly="false"` and `Private="false"`.
- [ ] `plugin.json` is unchanged (M03 owns the `Storage` capability addition).
- [ ] Lobby page (`DndMapperLobby.razor`) still renders unchanged; phase wiring lands in M04.

---

## 5. Manual verification

Not strictly required for M01 — the engine has no UI surface yet. Optional smoke test if desired:

- Run `dotnet run --project host/KnockBox/KnockBox.csproj`.
- Open two browsers, create a "DnD Mapper" room from the home page, join with the second browser.
- Confirm the lobby page still loads without errors. (No gameplay UI — the placeholder "Game scaffold — gameplay coming soon." is replaced by the lobby's existing content; phase wiring isn't here yet.)

---

## 6. Files NOT to create / modify (to avoid drift)

- `Pages/DndMapperRoom.razor` — DOES NOT EXIST YET; M04 creates it.
- `Pages/DndMapperPlayingPhase.razor` — M04.
- Any `Components/MapCanvas.razor`, `TokenLayer.razor`, `HostMapSwitcher.razor`, etc. — M04 / M05.
- `IGameEngineHttpHandler` — M02.
- `wwwroot/js/dndMapperTokenDrag.js` — M04.
- `plugin.json` — untouched; M03 adds `"Storage"` to `capabilities`.

---

## 7. Inline unit test plan

All tests live under `host/KnockBox.DndMapperTests/`. Mirror the per-verb test class structure used by `KnockBox.SpardleTests`. Use MSTest. Use `Moq` + `Moq.AutoMock` where needed.

### 7.1 Test fixtures / helpers

Create `Helpers/SequentialRng.cs` — a deterministic `IRandomNumberService` test double mirroring `KnockBox.SpardleTests/SequentialRng.cs`:

```csharp
internal sealed class SequentialRng : IRandomNumberService
{
    private readonly Queue<int> _values;
    public SequentialRng(params int[] values) => _values = new(values);
    public int GetRandomInt(int exclusiveMax, RandomType type = RandomType.Fast) => _values.Dequeue();
    public int GetRandomInt(int inclusiveMin, int exclusiveMax, RandomType type = RandomType.Fast) => _values.Dequeue();
    public byte[] GetRandomBytes(int length, RandomType type = RandomType.Fast) => new byte[length];
}
```

Create `Helpers/EngineTestFactory.cs` — a builder that returns a `DndMapperGameEngine` + a fresh `DndMapperGameState` with an injectable RNG and a host user. Use `UserFactory.Create()` for users.

### 7.2 Test classes & methods

Each `[TestMethod]` is a single-assertion unit test. The list below is the **minimum** coverage to merge M01.

#### `MapVerbsTests`

- `CreateMapAsync_HostCaller_AppendsToMaps` — happy path; verify `state.Maps.Count == 1`, `ListOrder == 0`.
- `CreateMapAsync_NonHostCaller_ReturnsError` — permission rejection.
- `RenameMapAsync_HostCaller_UpdatesName` — happy path.
- `RenameMapAsync_EmptyName_ReturnsError` — invalid input.
- `RenameMapAsync_UnknownMapId_ReturnsError` — invalid input.
- `DeleteMapAsync_HostCaller_RemovesMapFromList` — happy path; in-memory only.
- `DeleteMapAsync_DeletingActiveMap_ShiftsActiveMapIdToNextByListOrder` — verify `ActiveMapId` after delete points to the next map by `ListOrder`.
- `DeleteMapAsync_DeletingLastMap_SetsActiveMapIdToNull` — verify null when no maps remain.
- `DeleteMapAsync_NonHostCaller_ReturnsError`.
- `DuplicateMapAsync_HostCaller_DeepClonesGridButEmptyTokens` — verify the duplicate has the source's `Grid` values but `Tokens.Count == 0`.
- `ReorderMapsAsync_PermutationOfIds_UpdatesListOrder`.
- `ReorderMapsAsync_MissingId_ReturnsError`.
- `SetActiveMapAsync_HostCaller_UpdatesActiveMapId`.
- `SetActiveMapAsync_RegisteredPlayerWithoutTokenOnMap_AutoSpawnsPlayerToken` — register a player, set active map; assert a `PlayerToken` was added to `Map.Tokens` for that user at the spawn position.
- `SetActiveMapAsync_PlayerAlreadyHasTokenOnTargetMap_DoesNotDuplicate` — assert no extra token.
- `UpdateGridAsync_WidthBelowMinimum_ReturnsError` — try `WidthCells = 4`, expect failure.
- `UpdateGridAsync_HeightAboveMaximum_ReturnsError` — try `HeightCells = 201`, expect failure.
- `UpdateGridAsync_HostCaller_ReplacesGridConfig` — happy path.

#### `TokenVerbsTests`

- `SpawnPlayerTokenInternal_AssignsPaletteColorByPlayerSlot` — register two players, spawn both; assert tokens get palette indices 0 and 1.
- `SpawnPlayerTokenInternal_ReusesExistingSheetForPlayer` — spawn a player on map A, switch to map B and spawn; assert same `SheetId`.
- `SpawnNpcTokenAsync_HostCaller_CreatesNeutralColorToken`.
- `SpawnNpcTokenAsync_NonHostPlayerWithSettingDisabled_ReturnsError`.
- `SpawnNpcTokenAsync_NonHostPlayerWithSettingEnabled_AssignsCallerAsOwner`.
- `SpawnHostExtraTokenAsync_HostCaller_AssignsRepresentsUserIdColor` — spawn an extra "representing" a registered player; assert color matches the player's slot.
- `SpawnHostExtraTokenAsync_NonHostCaller_ReturnsError`.
- `MoveTokenAsync_OwnerOrHost_OwnerCanMoveOwnToken` — happy path with default `TokenMovement`.
- `MoveTokenAsync_OwnerOrHost_NonOwnerNonHostCannotMove`.
- `MoveTokenAsync_OwnerOrHost_HostCanMoveAnyToken`.
- `MoveTokenAsync_Anyone_PlayerCanMoveAnotherPlayersToken`.
- `MoveTokenAsync_HostOnly_PlayerCannotMoveOwnToken`.
- `MoveTokenAsync_OutOfBoundsCoordinates_ReturnsError`.
- `UpdateTokenAsync_HostCanUpdateAnyToken`.
- `UpdateTokenAsync_OwnerCanUpdateOwnPlayerToken`.
- `UpdateTokenAsync_NonOwnerNonHost_CannotUpdate`.
- `RemoveTokenAsync_HostCaller_RemovesToken`.
- `SetTokenHiddenAsync_HostCaller_FlipsHidden`.
- `SetTokenHiddenAsync_NonHostCaller_ReturnsError`.
- `PlayerUnregistered_ConvertsAllPlayerTokensToHostExtraTokens` — register a player, spawn tokens on two maps, simulate `PlayerUnregistered` (dispose the registration unsubscriber), assert `Type == HostExtraToken`, `OwnerUserId == null`, `RepresentsUserId == oldUserId`, sheets remain in `state.Sheets`.
- `RegisterPlayer_AfterStart_IsRejectedByPlatform` — call `state.RegisterPlayer(newUser)` after `StartAsyncCore` flips `IsJoinable` to false; assert the `ValueResult` is a failure (locks in the platform contract this milestone depends on).
- `RemoveTokenAsync_PlayerToken_ReturnsErrorEvenForHost` — explicit assertion of the §3.6 rule that `PlayerToken`s are never removable by this verb.

#### `SheetVerbsTests`

- `CreateSheetAsync_HostCaller_SeedsValuesFromSchema` — verify a fresh sheet has all schema rows seeded with their `Default` value.
- `UpdateSheetAttributeAsync_OwnersOnly_OwnerCanEditOwnSheet`.
- `UpdateSheetAttributeAsync_OwnersOnly_NonOwnerNonHostCannotEdit`.
- `UpdateSheetAttributeAsync_OwnersOnly_HostCanEditAnySheet` — host always exempt.
- `UpdateSheetAttributeAsync_Anyone_PlayerCanEditOthersSheet`.
- `UpdateSheetAttributeAsync_UnknownAttribute_ReturnsError`.
- `UpdateSheetAttributeAsync_TypeMismatch_ReturnsError` — try to set a `Score` row with a `Text` value.
- `UpdateSheetFreeFieldsAsync_NullableHpAccepted` — set `Hp = null` and `MaxHp = null`.
- `UpdateSheetFreeFieldsAsync_EmptyCharacterName_ReturnsError`.
- `DeleteSheetAsync_HostCaller_RemovesAndUnlinksTokens` — assert `SheetId` cleared on tokens that referenced it.
- `ChangeSchemaAsync_KeepsMatchingValueByName`.
- `ChangeSchemaAsync_TypeMismatch_ResetsToDefault`.
- `ChangeSchemaAsync_DropsUnknownAttributes`.

#### `SettingsTests`

- `UpdateSettingsAsync_HostCaller_ReplacesSettings`.
- `UpdateSettingsAsync_NonHostCaller_ReturnsError`.
- `UpdateSettingsAsync_DuringPlayingPhase_AllowedAndBroadcasts` — set settings after `StartAsyncCore`; assert no error and a state-change notification fires (subscribe to `state.StateChangedEventManager` in the test and count invocations).

#### `DiceTests`

Use `SequentialRng` with predetermined values to assert deterministic outcomes.

- `RollAsync_SingleD20_ReturnsTotal` — `(1, 20)` term, RNG yields 17 → total 17.
- `RollAsync_2d6_PlusFlatModifier_ReturnsTotal` — `(2, 6) + flat 3`; RNG yields 4, 5 → total 12.
- `RollAsync_AttributeRefOwnSheet_AddsModifier` — sheet has STR Score 14 (modifier +2); roll `1d20 + STR` with RNG yielding 10 → total 12.
- `RollAsync_AttributeRefForeignSheetByPlayer_ReturnsError` — caller is not the sheet owner and not the host.
- `RollAsync_AttributeRefForeignSheetByHost_Allowed`.
- `RollAsync_AttributeRefTextAttribute_ReturnsError`.
- `RollAsync_DieCountOverTwenty_ReturnsError` — `(21, 6)`.
- `RollAsync_AdvantageWithMultipleDieTerms_ReturnsError` — `(1,20) + (1,6)` with `Mode = Advantage`.
- `RollAsync_AdvantageWithSingleD20_KeepsHigher` — RNG yields 5 then 17 → total 17, with the 5 marked `Discarded`.
- `RollAsync_DisadvantageWithSingleD20_KeepsLower` — RNG yields 17 then 5 → total 5, with the 17 marked `Discarded`.
- `RollAsync_AppendsToRollLog`.
- `RollAsync_RollLogCappedAt200_DropsOldest` — invoke roll 201 times; assert `state.RollLog.Count == 200` and the oldest is gone.
- `RollAsync_ForcedByUserIdIsNullInV1` — explicit assertion that no v1 code path sets a non-null `ForcedByUserId`.

#### `LifecycleTests`

- `StartAsyncCore_FlipsPhaseToPlaying`.
- `StartAsyncCore_SetsActiveMapIdToFirstByListOrderIfUnset` — pre-author two maps with `ListOrder` 0 and 1; assert `ActiveMapId == map0.Id`.
- `StartAsyncCore_SpawnsPlayerTokensForRegisteredPlayers` — assert one `PlayerToken` per registered player on the active map.
- `StartAsyncCore_NoMaps_ActiveMapIdRemainsNull_NoTokensSpawned`.
- `EndSessionAsync_HostCaller_DisposesState` — subscribe to `state.OnStateDisposed`, call verb, assert handler fired.
- `EndSessionAsync_NonHostCaller_ReturnsError`.

### 7.3 Coverage rule

For every verb, a minimum of three test methods must exist:

1. **Happy path** under default settings.
2. **Permission rejection** — at least one off-path caller (player when host required, etc.).
3. **Invalid input** — at least one out-of-range / null / unknown-id / type-mismatch case.

A milestone-completion checklist for the implementer: enumerate every verb in §3.6 and confirm each has ≥ 3 corresponding test methods in §7.2.

---

## 8. Open questions / implementation choices to flag during PR

These are intentionally left to the implementer's judgment:

- **`Async` suffix on synchronous verbs**: the GDD names verbs `CreateMapAsync` etc. but `Execute` is synchronous. Match the GDD names for unambiguous cross-reference, returning `Result` (not `ValueTask<Result>`) where the body has nothing to await. Document this in a single comment near the engine class.
- **Duplicate-map image handling**: M01 leaves duplicate-image semantics intentionally undefined (the duplicate references the same `MapImage.RelativePath` strings? They'd share storage, but `RemoveImageAsync` on one would orphan the other). M01 ships with `DuplicateMapAsync` clearing `Images` (empty list) on the duplicate. M03 will revisit if cross-map-shared images become a requirement.
- **`MaxPlayerCount`**: 8 vs 6 — pick 8 for color-palette alignment unless `LobbyService` start-rules say otherwise.
