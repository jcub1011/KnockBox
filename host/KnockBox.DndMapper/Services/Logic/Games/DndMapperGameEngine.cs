using System.Collections.Immutable;
using System.Runtime.InteropServices;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Helpers;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.LoadedDice;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapper.Services.State.Games.Data.LoadedDice;

namespace KnockBox.DndMapper.Services.Logic.Games
{
    // M01 verb names use the GDD's `Async` suffix for cross-reference with the design doc,
    // but the bodies are synchronous (Execute is sync). Return types are plain Result / ValueResult<T>.
    public sealed class DndMapperGameEngine : AbstractGameEngine
    {
        private const int MaxRollDiceCount = 20;
        private const int MaxNameLength = 60;
        private static readonly int[] AllowedDieSides = [4, 6, 8, 10, 12, 20, 100];

        // Image caps — bytes never reach the server now; host's IndexedDB owns the blobs
        // and the server only tracks metadata + the published share token. The caps still
        // enforce a 100 MB-per-file / 1 GB-per-room budget on what's *referenced* by state
        // so a misbehaving caller can't balloon AbstractGameState.
        internal const long PerFileCapBytes = 100L * 1024 * 1024;
        internal const long PerRoomCapBytes = 1024L * 1024 * 1024;

        internal static readonly IReadOnlyList<string> AllowedImageContentTypeList =
            ["image/png", "image/jpeg", "image/webp"];
        private static readonly HashSet<string> AllowedImageContentTypes = new(AllowedImageContentTypeList, StringComparer.OrdinalIgnoreCase);

        private readonly ILogger<DndMapperGameEngine> _logger;
        private readonly ILogger<DndMapperGameState> _stateLogger;
        private readonly IRandomNumberService _rng;

        public DndMapperGameEngine(
            ILogger<DndMapperGameEngine> logger,
            ILogger<DndMapperGameState> stateLogger,
            IRandomNumberService rng)
            : base(maxPlayerCount: 8, minPlayerCount: 1)
        {
            _logger = logger;
            _stateLogger = stateLogger;
            _rng = rng;
        }

        public override Task<ValueResult<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default)
        {
            if (host is null)
                return Task.FromResult(ValueResult<AbstractGameState>.FromError(
                    "Failed to create game state.",
                    $"Parameter {nameof(host)} was null."));

            var state = new DndMapperGameState(host, _stateLogger);
            state.Execute(() => state.SetJoinable(true));
            state.SubscribePlayerUnregistered(player => HandlePlayerLeft(state, player));
            _logger.LogInformation("Created DnD Mapper game state with host [{userId}].", host.Id);
            return Task.FromResult<ValueResult<AbstractGameState>>(state);
        }

        protected override Task<Result> StartAsyncCore(AbstractGameState abstractState, CancellationToken ct = default)
        {
            if (abstractState is not DndMapperGameState state)
                return Task.FromResult(Result.FromError(
                    "Error starting game.",
                    $"Game state of type [{(abstractState?.GetType().Name ?? "null")}] couldn't be cast to type [{nameof(DndMapperGameState)}]."));

            var executeResult = state.Execute(() =>
            {
                state.SetPhase(DndMapperPhase.Playing);
                state.SetJoinable(false);

                if (state.ActiveMapId is null && state.Maps.Length > 0)
                    state.SetActiveMapId(state.Maps.OrderBy(m => m.ListOrder).First().Id);

                if (state.ActiveMapId is Guid mapId &&
                    state.Maps.FirstOrDefault(m => m.Id == mapId) is Map activeMap)
                {
                    foreach (var entry in state.Players)
                    {
                        SpawnPlayerTokenInternal(state, activeMap.Id, entry.User);
                    }
                }
            });

            return Task.FromResult(executeResult);
        }

        // ── Player lifecycle ──────────────────────────────────────────────────────

        private void HandlePlayerLeft(DndMapperGameState state, User player)
        {
            // PlayerUnregistered fires OUTSIDE the lock — re-entering Execute is safe.
            state.Execute(() => ConvertAbandonedPlayerCharacterInternal(state, player));
        }

        // ── Map verbs ─────────────────────────────────────────────────────────────

        public ValueResult<Guid> CreateMapAsync(DndMapperGameState state, User caller, string name)
        {
            if (state is null) return ValueResult<Guid>.FromError("State is required.");
            if (caller is null) return ValueResult<Guid>.FromError("Caller is required.");
            if (string.IsNullOrWhiteSpace(name)) return ValueResult<Guid>.FromError("Map name cannot be empty.");
            if (name.Length > MaxNameLength) return ValueResult<Guid>.FromError($"Map name cannot exceed {MaxNameLength} characters.");
            if (!IsHost(state, caller)) return ValueResult<Guid>.FromError("Only the host may create maps.");

            var newId = Guid.NewGuid();
            var exec = state.Execute(() =>
            {
                state.Maps = state.Maps.Add(new Map
                {
                    Id = newId,
                    Name = name,
                    ListOrder = state.Maps.Length,
                    CreatedUtc = DateTime.UtcNow,
                });
            });

            if (exec.IsCanceled) return ValueResult<Guid>.FromCancellation();
            if (exec.TryGetFailure(out var err)) return ValueResult<Guid>.FromError(err);
            return ValueResult<Guid>.FromValue(newId);
        }

        public Result RenameMapAsync(DndMapperGameState state, User caller, Guid mapId, string newName)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (string.IsNullOrWhiteSpace(newName)) return Result.FromError("Map name cannot be empty.");
            if (newName.Length > MaxNameLength) return Result.FromError($"Map name cannot exceed {MaxNameLength} characters.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may rename maps.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (!state.UpdateMap(mapId, m => m with { Name = newName }))
                    error = "Unknown map id.";
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public Result DeleteMapAsync(DndMapperGameState state, User caller, Guid mapId)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may delete maps.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                var idx = state.IndexOfMap(mapId);
                if (idx < 0) { error = "Unknown map id."; return; }
                var map = state.Maps[idx];

                long deltaBytes = 0;
                foreach (var image in map.Images)
                    deltaBytes += image.ByteSize;
                if (deltaBytes > 0) state.AdjustBytesUsed(-deltaBytes);

                state.Maps = state.Maps.RemoveAt(idx);

                if (state.ActiveMapId == mapId)
                {
                    var next = state.Maps.OrderBy(m => m.ListOrder).FirstOrDefault();
                    state.SetActiveMapId(next?.Id);
                }
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public ValueResult<Guid> DuplicateMapAsync(DndMapperGameState state, User caller, Guid mapId)
        {
            if (state is null) return ValueResult<Guid>.FromError("State is required.");
            if (caller is null) return ValueResult<Guid>.FromError("Caller is required.");
            if (!IsHost(state, caller)) return ValueResult<Guid>.FromError("Only the host may duplicate maps.");

            Guid newId = default;
            string? error = null;
            var exec = state.Execute(() =>
            {
                var source = state.Maps.FirstOrDefault(m => m.Id == mapId);
                if (source is null) { error = "Unknown map id."; return; }

                newId = Guid.NewGuid();
                var clone = new Map
                {
                    Id = newId,
                    Name = $"{source.Name} (copy)",
                    Grid = source.Grid,
                    ListOrder = state.Maps.Length,
                    CreatedUtc = DateTime.UtcNow,
                    DefaultSpawnPosition = source.DefaultSpawnPosition,
                };
                state.Maps = state.Maps.Add(clone);
            });

            if (exec.IsCanceled) return ValueResult<Guid>.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return ValueResult<Guid>.FromError(execErr);
            if (error is not null) return ValueResult<Guid>.FromError(error);
            return ValueResult<Guid>.FromValue(newId);
        }

        public Result ReorderMapsAsync(DndMapperGameState state, User caller, IReadOnlyList<Guid> orderedIds)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (orderedIds is null) return Result.FromError("Ordered ids are required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may reorder maps.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                var existingIds = state.Maps.Select(m => m.Id).ToHashSet();
                var providedIds = new HashSet<Guid>(orderedIds);
                if (existingIds.Count != providedIds.Count || !existingIds.SetEquals(providedIds))
                {
                    error = "Reorder must include exactly the existing map ids.";
                    return;
                }

                var orderByIndex = new Dictionary<Guid, int>(orderedIds.Count);
                for (int i = 0; i < orderedIds.Count; i++) orderByIndex[orderedIds[i]] = i;
                state.Maps = state.Maps
                    .Select(m => m with { ListOrder = orderByIndex[m.Id] })
                    .ToImmutableArray();
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public Result SetActiveMapAsync(DndMapperGameState state, User caller, Guid mapId)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may switch the active map.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                var map = state.Maps.FirstOrDefault(m => m.Id == mapId);
                if (map is null) { error = "Unknown map id."; return; }

                state.SetActiveMapId(mapId);

                foreach (var entry in state.Players)
                {
                    // Re-fetch in case a prior spawn this loop replaced the map.
                    var liveMap = state.Maps.FirstOrDefault(m => m.Id == mapId);
                    if (liveMap is null) break;
                    if (!liveMap.Tokens.Any(t => t.Type == TokenType.PlayerToken && t.OwnerUserId == entry.User.Id))
                        SpawnPlayerTokenInternal(state, mapId, entry.User);
                }
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public Result UpdateGridAsync(DndMapperGameState state, User caller, Guid mapId, GridConfig newGrid)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (newGrid is null) return Result.FromError("Grid is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may update grid configuration.");
            if (newGrid.WidthCells < 1 || newGrid.WidthCells > 1000)
                return Result.FromError("Grid width must be between 1 and 1000 cells.");
            if (newGrid.HeightCells < 1 || newGrid.HeightCells > 1000)
                return Result.FromError("Grid height must be between 1 and 1000 cells.");
            if (newGrid.CellPixels < 1 || newGrid.CellPixels > 1000)
                return Result.FromError("Cell pixel size must be between 1 and 1000.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (!state.UpdateMap(mapId, m =>
                {
                    bool gridDimsChanged = m.Grid.WidthCells != newGrid.WidthCells
                        || m.Grid.HeightCells != newGrid.HeightCells;
                    // Mask bit layout is keyed off WidthCells; treat any dim change as a
                    // fog mutation so canvas memoization rebuilds the polygon.
                    var nextFogVersion = (gridDimsChanged && !m.FogMask.IsDefaultOrEmpty)
                        ? m.FogVersion + 1
                        : m.FogVersion;

                    // Tokens are stored at cell-center coordinates; clamp any that fall
                    // outside the new bounds to the nearest in-bounds cell center.
                    // Images are intentionally left as-is so the host can reposition them.
                    double maxX = Math.Max(0.5, newGrid.WidthCells - 0.5);
                    double maxY = Math.Max(0.5, newGrid.HeightCells - 0.5);
                    var newTokens = m.Tokens;
                    if (m.Tokens.Length > 0)
                    {
                        newTokens = m.Tokens
                            .Select(t => t with
                            {
                                X = Math.Clamp(t.X, 0.5, maxX),
                                Y = Math.Clamp(t.Y, 0.5, maxY),
                            })
                            .ToImmutableArray();
                    }

                    return m with
                    {
                        Grid = newGrid,
                        FogVersion = nextFogVersion,
                        Tokens = newTokens,
                    };
                }))
                {
                    error = "Unknown map id.";
                }
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        // ── Token verbs ───────────────────────────────────────────────────────────

        public ValueResult<Guid> SpawnPlayerTokenAsync(DndMapperGameState state, User caller, string userId)
        {
            if (state is null) return ValueResult<Guid>.FromError("State is required.");
            if (caller is null) return ValueResult<Guid>.FromError("Caller is required.");
            if (string.IsNullOrWhiteSpace(userId)) return ValueResult<Guid>.FromError("User id is required.");

            bool isHost = IsHost(state, caller);
            bool isSelf = caller.Id == userId;
            if (!isHost && !isSelf) return ValueResult<Guid>.FromError("Players may only spawn their own player token.");

            Guid newTokenId = default;
            string? error = null;
            var exec = state.Execute(() =>
            {
                if (state.ActiveMapId is not Guid mapId ||
                    state.Maps.FirstOrDefault(m => m.Id == mapId) is null)
                {
                    error = "No active map.";
                    return;
                }

                User? targetUser = state.Players
                    .FirstOrDefault(p => p.User.Id == userId).User;
                if (targetUser is null)
                {
                    error = "Target user is not a registered player.";
                    return;
                }

                newTokenId = SpawnPlayerTokenInternal(state, mapId, targetUser);
            });

            if (exec.IsCanceled) return ValueResult<Guid>.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return ValueResult<Guid>.FromError(execErr);
            if (error is not null) return ValueResult<Guid>.FromError(error);
            return ValueResult<Guid>.FromValue(newTokenId);
        }

        /// <summary>
        /// Spawns an NPC on the given map. When <paramref name="representsUserId"/>
        /// is non-null the NPC is a "stand-in" for that player (a DMPC, or the orphan
        /// of a player who left mid-session) and the caller must be the host. When
        /// it's null the NPC is a regular non-player creature; the host may always
        /// create one, players may create one only when
        /// <c>DndMapperSettings.PlayersCanCreateNPCs</c> is set.
        /// </summary>
        public ValueResult<Guid> SpawnNpcTokenAsync(
            DndMapperGameState state,
            User caller,
            Guid mapId,
            string name,
            string? representsUserId = null,
            double? atX = null,
            double? atY = null)
        {
            if (state is null) return ValueResult<Guid>.FromError("State is required.");
            if (caller is null) return ValueResult<Guid>.FromError("Caller is required.");
            if (string.IsNullOrWhiteSpace(name)) return ValueResult<Guid>.FromError("Token name is required.");

            bool isHost = IsHost(state, caller);
            if (representsUserId is not null && !isHost)
                return ValueResult<Guid>.FromError("Only the host may spawn an NPC that represents a player.");
            if (!isHost && !state.Settings.PlayersCanCreateNPCs)
                return ValueResult<Guid>.FromError("Players are not permitted to create NPC tokens.");
            if (!isHost && !state.Players.Any(p => p.User.Id == caller.Id))
                return ValueResult<Guid>.FromError("Only registered players or the host may create NPC tokens.");

            Guid newId = default;
            string? error = null;
            var exec = state.Execute(() =>
            {
                var mapIdx = state.IndexOfMap(mapId);
                if (mapIdx < 0) { error = "Unknown map id."; return; }
                var map = state.Maps[mapIdx];

                string color = DefaultColorPalette.FromName(name);

                newId = Guid.NewGuid();
                var (cx, cy) = ResolveSpawn(map, atX, atY);
                var newToken = new Token
                {
                    Id = newId,
                    Type = TokenType.NPCToken,
                    OwnerUserId = isHost ? null : caller.Id,
                    RepresentsUserId = representsUserId,
                    Name = name,
                    Color = color,
                    IconKind = TokenIconKind.Initial,
                    MapId = mapId,
                    X = cx,
                    Y = cy,
                };
                state.Maps = state.Maps.SetItem(mapIdx, map with { Tokens = map.Tokens.Add(newToken) });
            });

            if (exec.IsCanceled) return ValueResult<Guid>.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return ValueResult<Guid>.FromError(execErr);
            if (error is not null) return ValueResult<Guid>.FromError(error);
            return ValueResult<Guid>.FromValue(newId);
        }

        public Result MoveTokenAsync(DndMapperGameState state, User caller, Guid tokenId, double newX, double newY)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                var (token, map) = FindTokenAndMap(state, tokenId);
                if (token is null || map is null) { error = "Unknown token id."; return; }

                if (!CanMoveToken(state, caller, token)) { error = "You are not permitted to move this token."; return; }

                if (newX < 0 || newX > map.Grid.WidthCells || newY < 0 || newY > map.Grid.HeightCells)
                {
                    error = "Position is out of map bounds.";
                    return;
                }

                // Authoritative snap: clients always send pre-snapped coordinates,
                // but a stale client or stack-offset preview can land between cells.
                // Re-applying server-side guarantees tokens come to rest on cell
                // centers whenever the map's grid has SnapToGrid enabled.
                var (sx, sy) = SnapToGridHelper.Snap(newX, newY, map.Grid);
                state.UpdateToken(map.Id, tokenId, t => t with { X = sx, Y = sy });
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public Result UpdateTokenAsync(DndMapperGameState state, User caller, Guid tokenId, string name, string color, TokenIconKind iconKind)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (string.IsNullOrWhiteSpace(name)) return Result.FromError("Token name cannot be empty.");
            if (string.IsNullOrWhiteSpace(color)) return Result.FromError("Token color cannot be empty.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                var (token, map) = FindTokenAndMap(state, tokenId);
                if (token is null || map is null) { error = "Unknown token id."; return; }

                bool isHost = IsHost(state, caller);
                bool isOwner = token.OwnerUserId is not null && token.OwnerUserId == caller.Id;
                if (!isHost && !isOwner) { error = "You are not permitted to update this token."; return; }

                state.UpdateToken(map.Id, tokenId, t => t with
                {
                    Name = name,
                    Color = color,
                    IconKind = iconKind,
                });

                // Propagate to a linked sheet so the two stay in sync. Guarded
                // by value-compare to avoid pointless StateChanged churn.
                if (token.SheetId is Guid linkedSheetId
                    && state.Sheets.TryGetValue(linkedSheetId, out var linkedSheet)
                    && linkedSheet.CharacterName != name)
                {
                    state.UpdateSheet(linkedSheetId, s => s with { CharacterName = name });
                }
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public Result RemoveTokenAsync(DndMapperGameState state, User caller, Guid tokenId)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                var (token, map) = FindTokenAndMap(state, tokenId);
                if (token is null || map is null) { error = "Unknown token id."; return; }

                if (token.Type == TokenType.PlayerToken)
                {
                    error = "Player tokens cannot be removed directly; they are managed automatically.";
                    return;
                }

                bool isHost = IsHost(state, caller);
                bool isNpcOwner = token.Type == TokenType.NPCToken
                    && token.OwnerUserId is not null
                    && token.OwnerUserId == caller.Id;
                if (!isHost && !isNpcOwner)
                {
                    error = "You are not permitted to remove this token.";
                    return;
                }

                var mapIdx = state.IndexOfMap(map.Id);
                var tokenIdx = DndMapperGameState.IndexOfToken(map, tokenId);
                state.Maps = state.Maps.SetItem(mapIdx, map with { Tokens = map.Tokens.RemoveAt(tokenIdx) });
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        /// <summary>
        /// Sets or clears the character sheet attached to a token. Host-only.
        /// Pass <c>null</c> to detach. The sheet must exist if non-null. Rejects
        /// attaching a player-owned sheet to an NPC (player sheets are personal to
        /// their owner) or attaching a sheet that's already linked to
        /// a different token (one character per sheet).
        /// </summary>
        public Result SetTokenSheetAsync(DndMapperGameState state, User caller, Guid tokenId, Guid? sheetId)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may attach character sheets to tokens.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                var (token, map) = FindTokenAndMap(state, tokenId);
                if (token is null || map is null) { error = "Unknown token id."; return; }
                if (sheetId is Guid sid)
                {
                    if (!state.Sheets.TryGetValue(sid, out var sheet))
                    {
                        error = "Unknown sheet id.";
                        return;
                    }
                    if (sheet.OwnerUserId is not null)
                    {
                        error = "That sheet belongs to a player.";
                        return;
                    }
                    var attachedElsewhere = state.Maps
                        .SelectMany(m => m.Tokens)
                        .Any(other => other.Id != tokenId && other.SheetId == sid);
                    if (attachedElsewhere)
                    {
                        error = "That sheet is already attached to another token.";
                        return;
                    }

                    // Name inheritance on link: prefer the sheet name when it has one
                    // (resolves the "both already named" case in the sheet's favor);
                    // otherwise fall back to copying the token name onto the sheet.
                    string nextTokenName = token.Name;
                    string nextSheetCharacterName = sheet.CharacterName;
                    if (!string.IsNullOrWhiteSpace(sheet.CharacterName)
                        && token.Name != sheet.CharacterName)
                    {
                        nextTokenName = sheet.CharacterName;
                    }
                    else if (!string.IsNullOrWhiteSpace(token.Name)
                        && sheet.CharacterName != token.Name)
                    {
                        nextSheetCharacterName = token.Name;
                    }

                    string? nextSheetOwner = sheet.OwnerUserId;
                    string? nextSheetRepresents = sheet.RepresentsUserId;
                    // Attaching a sheet to a player-owned token transfers
                    // sheet ownership to that player so the sheet shows up in
                    // their character-sheet panel. Reject if the target player
                    // already owns a different sheet (one character per player).
                    if (token.Type == TokenType.PlayerToken && token.OwnerUserId is string ownerId)
                    {
                        if (state.Sheets.Values.Any(s => s.OwnerUserId == ownerId && s.Id != sid))
                        {
                            error = "Target player already owns a character sheet.";
                            return;
                        }
                        nextSheetOwner = ownerId;
                        nextSheetRepresents = null;
                    }

                    if (!ReferenceEquals(nextTokenName, token.Name))
                        state.UpdateToken(map.Id, tokenId, t => t with { Name = nextTokenName });
                    if (nextSheetCharacterName != sheet.CharacterName
                        || nextSheetOwner != sheet.OwnerUserId
                        || nextSheetRepresents != sheet.RepresentsUserId)
                    {
                        state.UpdateSheet(sid, s => s with
                        {
                            CharacterName = nextSheetCharacterName,
                            OwnerUserId = nextSheetOwner,
                            RepresentsUserId = nextSheetRepresents,
                        });
                    }
                }
                state.UpdateToken(map.Id, tokenId, t => t with { SheetId = sheetId });
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        /// <summary>
        /// Sets or clears the player an NPC token represents. Host-only.
        /// Not valid on <see cref="TokenType.PlayerToken"/> (player tokens are
        /// auto-managed). Pass <c>null</c> to clear.
        /// </summary>
        public Result SetTokenRepresentsAsync(DndMapperGameState state, User caller, Guid tokenId, string? representsUserId)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may change which player a token represents.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                var (token, map) = FindTokenAndMap(state, tokenId);
                if (token is null || map is null) { error = "Unknown token id."; return; }
                if (token.Type == TokenType.PlayerToken)
                {
                    error = "Player tokens cannot represent another player.";
                    return;
                }
                if (representsUserId is not null
                    && !state.Players.Any(p => p.User.Id == representsUserId))
                {
                    error = "Unknown player id.";
                    return;
                }
                state.UpdateToken(map.Id, tokenId, t => t with { RepresentsUserId = representsUserId });
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public Result SetTokenHiddenAsync(DndMapperGameState state, User caller, Guid tokenId, bool hidden)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may toggle token visibility.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                var (token, map) = FindTokenAndMap(state, tokenId);
                if (token is null || map is null) { error = "Unknown token id."; return; }
                state.UpdateToken(map.Id, tokenId, t => t with { Hidden = hidden });
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        // ── Sheet verbs ───────────────────────────────────────────────────────────

        public ValueResult<Guid> CreateSheetAsync(DndMapperGameState state, User caller, string? ownerUserId, string characterName)
        {
            if (state is null) return ValueResult<Guid>.FromError("State is required.");
            if (caller is null) return ValueResult<Guid>.FromError("Caller is required.");
            if (string.IsNullOrWhiteSpace(characterName)) return ValueResult<Guid>.FromError("Character name cannot be empty.");

            // Host can create any sheet (NPC or owned). A non-host player may only
            // create a single sheet for themselves; once they have one they must use
            // the existing sheet rather than spawn duplicates.
            // The duplicate check runs *inside* state.Execute so two concurrent
            // requests can't both pass the guard and end up with two sheets.
            if (!IsHost(state, caller) && ownerUserId != caller.Id)
                return ValueResult<Guid>.FromError("Players may only create a sheet they own.");

            Guid newId = Guid.NewGuid();
            string? error = null;
            var exec = state.Execute(() =>
            {
                if (!IsHost(state, caller) && state.Sheets.Values.Any(s => s.OwnerUserId == caller.Id))
                {
                    error = "You already have a character sheet.";
                    return;
                }

                var sheet = new CharacterSheet
                {
                    Id = newId,
                    OwnerUserId = ownerUserId,
                    CharacterName = characterName,
                    Color = DefaultColorPalette.FromName(characterName),
                    Values = SeedSheetValues(state.AttributeSchema),
                };
                state.Sheets = state.Sheets.SetItem(newId, sheet);
            });

            if (exec.IsCanceled) return ValueResult<Guid>.FromCancellation();
            if (exec.TryGetFailure(out var err)) return ValueResult<Guid>.FromError(err);
            if (error is not null) return ValueResult<Guid>.FromError(error);
            return ValueResult<Guid>.FromValue(newId);
        }

        public Result UpdateSheetAttributeAsync(DndMapperGameState state, User caller, Guid sheetId, string attributeName, AttributeValue value)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (string.IsNullOrWhiteSpace(attributeName)) return Result.FromError("Attribute name is required.");
            if (value is null) return Result.FromError("Value is required.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (!state.Sheets.TryGetValue(sheetId, out var sheet)) { error = "Unknown sheet id."; return; }
                if (!CanEditSheet(state, caller, sheet)) { error = "You are not permitted to edit this sheet."; return; }

                var row = state.AttributeSchema.Rows.FirstOrDefault(r => string.Equals(r.Name, attributeName, StringComparison.Ordinal));
                if (row is null) { error = "Unknown attribute."; return; }
                if (row.Type != value.Type) { error = "Attribute value type does not match the schema."; return; }

                state.UpdateSheet(sheetId, s => s with { Values = s.Values.SetItem(attributeName, value) });
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public Result UpdateSheetFreeFieldsAsync(DndMapperGameState state, User caller, Guid sheetId, string characterName, string notes, int? hp, int? maxHp)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (string.IsNullOrWhiteSpace(characterName)) return Result.FromError("Character name cannot be empty.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (!state.Sheets.TryGetValue(sheetId, out var sheet)) { error = "Unknown sheet id."; return; }
                if (!CanEditSheet(state, caller, sheet)) { error = "You are not permitted to edit this sheet."; return; }

                state.UpdateSheet(sheetId, s => s with
                {
                    CharacterName = characterName,
                    Notes = notes ?? string.Empty,
                    Hp = hp,
                    MaxHp = maxHp,
                });

                // Propagate rename to every linked token so the token roster
                // and SVG label stay in sync with the sheet.
                state.Maps = state.Maps
                    .Select(m =>
                    {
                        var anyChange = false;
                        var newTokens = m.Tokens;
                        for (int i = 0; i < newTokens.Length; i++)
                        {
                            var token = newTokens[i];
                            if (token.SheetId == sheetId && token.Name != characterName)
                            {
                                newTokens = newTokens.SetItem(i, token with { Name = characterName });
                                anyChange = true;
                            }
                        }
                        return anyChange ? m with { Tokens = newTokens } : m;
                    })
                    .ToImmutableArray();
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public Result UpdateSheetArmorClassAsync(DndMapperGameState state, User caller, Guid sheetId, int? armorClass)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (!state.Sheets.TryGetValue(sheetId, out var sheet)) { error = "Unknown sheet id."; return; }
                if (!CanEditSheet(state, caller, sheet)) { error = "You are not permitted to edit this sheet."; return; }

                state.UpdateSheet(sheetId, s => s with { ArmorClass = armorClass });
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public Result UpdateSheetColorAsync(DndMapperGameState state, User caller, Guid sheetId, string color)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (!state.Sheets.TryGetValue(sheetId, out var sheet)) { error = "Unknown sheet id."; return; }
                if (!CanEditSheet(state, caller, sheet)) { error = "You are not permitted to edit this sheet."; return; }

                state.UpdateSheet(sheetId, s => s with { Color = color ?? string.Empty });
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public Result UpdateSheetScopeAsync(DndMapperGameState state, User caller, Guid sheetId, Guid? scopedMapId)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may change sheet scope.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (!state.Sheets.ContainsKey(sheetId)) { error = "Unknown sheet id."; return; }
                if (scopedMapId is Guid mid && state.Maps.All(m => m.Id != mid))
                {
                    error = "Unknown map id.";
                    return;
                }
                state.UpdateSheet(sheetId, s => s with { ScopedMapId = scopedMapId });
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public ValueResult<Guid> DuplicateSheetAsync(DndMapperGameState state, User caller, Guid sourceSheetId)
        {
            if (state is null) return ValueResult<Guid>.FromError("State is required.");
            if (caller is null) return ValueResult<Guid>.FromError("Caller is required.");
            if (!IsHost(state, caller)) return ValueResult<Guid>.FromError("Only the host may duplicate sheets.");

            Guid newId = default;
            string? error = null;
            var exec = state.Execute(() =>
            {
                if (!state.Sheets.TryGetValue(sourceSheetId, out var source)) { error = "Unknown sheet id."; return; }

                newId = Guid.NewGuid();
                // Status effects and roll templates are immutable lists carrying
                // ids that are fine to share — they're keyed independently of the
                // sheet. Owner / Represents intentionally clear so the duplicate
                // starts as a host-owned NPC the host can reassign.
                var clone = source with
                {
                    Id = newId,
                    CharacterName = $"{source.CharacterName} (copy)",
                    OwnerUserId = null,
                    RepresentsUserId = null,
                };
                state.Sheets = state.Sheets.SetItem(newId, clone);
            });

            if (exec.IsCanceled) return ValueResult<Guid>.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return ValueResult<Guid>.FromError(execErr);
            if (error is not null) return ValueResult<Guid>.FromError(error);
            return ValueResult<Guid>.FromValue(newId);
        }

        public ValueResult<Guid> CreateTokenForSheetOnMapAsync(DndMapperGameState state, User caller, Guid sheetId, Guid mapId)
        {
            if (state is null) return ValueResult<Guid>.FromError("State is required.");
            if (caller is null) return ValueResult<Guid>.FromError("Caller is required.");
            if (!IsHost(state, caller)) return ValueResult<Guid>.FromError("Only the host may create tokens for a sheet.");

            Guid tokenId = Guid.Empty;
            string? error = null;
            var exec = state.Execute(() =>
            {
                if (!state.Sheets.TryGetValue(sheetId, out var sheet)) { error = "Unknown sheet id."; return; }

                var mapIdx = state.IndexOfMap(mapId);
                if (mapIdx < 0) { error = "Unknown map id."; return; }
                var map = state.Maps[mapIdx];
                var (sx, sy) = SpawnPosition(map);

                tokenId = Guid.NewGuid();
                // PlayerToken when the sheet has a live owner, NPCToken otherwise.
                // RepresentsUserId mirrors the sheet so the "represents …" label
                // round-trips into the new token without a separate engine call.
                var hasLiveOwner = sheet.OwnerUserId is { } ouid
                    && state.Players.Any(p => p.User.Id == ouid);
                var token = new Token
                {
                    Id = tokenId,
                    Type = hasLiveOwner ? TokenType.PlayerToken : TokenType.NPCToken,
                    OwnerUserId = hasLiveOwner ? sheet.OwnerUserId : null,
                    RepresentsUserId = sheet.RepresentsUserId,
                    Name = sheet.CharacterName,
                    // Token.Color is the fallback when sheet color isn't set.
                    // Render sites prefer sheet.Color via Token.ResolveColor.
                    Color = string.IsNullOrEmpty(sheet.Color)
                        ? DefaultColorPalette.FromName(sheet.CharacterName)
                        : sheet.Color,
                    IconKind = TokenIconKind.Initial,
                    MapId = mapId,
                    X = sx,
                    Y = sy,
                    SheetId = sheetId,
                };
                state.Maps = state.Maps.SetItem(mapIdx, map with { Tokens = map.Tokens.Add(token) });
            });

            if (exec.IsCanceled) return ValueResult<Guid>.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return ValueResult<Guid>.FromError(execErr);
            if (error is not null) return ValueResult<Guid>.FromError(error);
            return ValueResult<Guid>.FromValue(tokenId);
        }

        public Result DeleteSheetAsync(DndMapperGameState state, User caller, Guid sheetId)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may delete sheets.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (!state.Sheets.ContainsKey(sheetId)) { error = "Unknown sheet id."; return; }
                state.Sheets = state.Sheets.Remove(sheetId);

                state.Maps = state.Maps
                    .Select(m =>
                    {
                        var anyChange = false;
                        var newTokens = m.Tokens;
                        for (int i = 0; i < newTokens.Length; i++)
                        {
                            var token = newTokens[i];
                            if (token.SheetId == sheetId)
                            {
                                newTokens = newTokens.SetItem(i, token with { SheetId = null });
                                anyChange = true;
                            }
                        }
                        return anyChange ? m with { Tokens = newTokens } : m;
                    })
                    .ToImmutableArray();
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public Result ChangeSchemaAsync(DndMapperGameState state, User caller, AttributeSchema newSchema, Guid? sourceTemplateId = null)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (newSchema is null) return Result.FromError("Schema is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may change the attribute schema.");

            var exec = state.Execute(() =>
            {
                state.SetAttributeSchema(newSchema);

                // Caller-supplied id wins; otherwise infer from the preset
                // (built-in presets resolve to their deterministic ids, Custom
                // resolves to null — a free-form schema with no library bucket).
                var resolvedId = sourceTemplateId
                    ?? DndMapperGameState.BuiltInTemplateIdFor(newSchema.Preset);
                if (resolvedId is { } id && !state.CustomTemplates.ContainsKey(id))
                    resolvedId = null;
                state.SetActiveSchemaTemplateId(resolvedId);

                // Re-derive the initiative attribute for the new schema:
                //   1. If we landed on a template, take its preferred attribute.
                //   2. Otherwise keep the current attribute if the new schema
                //      still has a matching row (free-form edits that don't
                //      drop the chosen attribute).
                //   3. Fall back to "DEX" if the schema has one (legacy d20
                //      convention), else null.
                var schemaRowNames = newSchema.Rows.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
                string? nextInitiative;
                if (resolvedId is { } rid && state.CustomTemplates.TryGetValue(rid, out var resolvedTemplate))
                    nextInitiative = resolvedTemplate.InitiativeAttributeName;
                else if (state.InitiativeAttributeName is { } existing && schemaRowNames.Contains(existing))
                    nextInitiative = existing;
                else if (newSchema.Rows.Any(r => string.Equals(r.Name, "DEX", StringComparison.OrdinalIgnoreCase)))
                    nextInitiative = "DEX";
                else
                    nextInitiative = null;
                state.SetInitiativeAttributeName(nextInitiative);

                state.Sheets = state.Sheets.ToImmutableDictionary(
                    kv => kv.Key,
                    kv => RemapSheetValues(kv.Value, newSchema));
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var err)) return Result.FromError(err);
            return Result.Success;
        }

        private static CharacterSheet RemapSheetValues(CharacterSheet sheet, AttributeSchema newSchema)
        {
            var builder = ImmutableDictionary.CreateBuilder<string, AttributeValue>();
            foreach (var row in newSchema.Rows)
            {
                if (sheet.Values.TryGetValue(row.Name, out var existing) && existing.Type == row.Type)
                    builder[row.Name] = existing;
                else
                    builder[row.Name] = row.Default;
            }
            return sheet with { Values = builder.ToImmutable() };
        }

        // ── Named-template verbs ─────────────────────────────────────────────────

        public ValueResult<Guid> SaveCustomTemplateAsync(DndMapperGameState state, User caller, string name)
        {
            if (state is null) return ValueResult<Guid>.FromError("State is required.");
            if (caller is null) return ValueResult<Guid>.FromError("Caller is required.");
            if (string.IsNullOrWhiteSpace(name)) return ValueResult<Guid>.FromError("Template name cannot be empty.");
            if (name.Length > MaxNameLength) return ValueResult<Guid>.FromError($"Template name cannot exceed {MaxNameLength} characters.");
            if (!IsHost(state, caller)) return ValueResult<Guid>.FromError("Only the host may save attribute templates.");

            var trimmed = name.Trim();
            var newId = Guid.NewGuid();
            string? error = null;
            var exec = state.Execute(() =>
            {
                if (state.CustomTemplates.Values.Any(t =>
                        string.Equals(t.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
                {
                    error = "A template with that name already exists.";
                    return;
                }

                state.CustomTemplates = state.CustomTemplates.SetItem(newId, new NamedTemplate
                {
                    Id = newId,
                    Name = trimmed,
                    Rows = [.. state.AttributeSchema.Rows],
                });
            });

            if (exec.IsCanceled) return ValueResult<Guid>.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return ValueResult<Guid>.FromError(execErr);
            if (error is not null) return ValueResult<Guid>.FromError(error);
            return ValueResult<Guid>.FromValue(newId);
        }

        public ValueResult<Guid> CreateCustomTemplateAsync(DndMapperGameState state, User caller, string name, IReadOnlyList<AttributeRow> rows)
        {
            if (state is null) return ValueResult<Guid>.FromError("State is required.");
            if (caller is null) return ValueResult<Guid>.FromError("Caller is required.");
            if (string.IsNullOrWhiteSpace(name)) return ValueResult<Guid>.FromError("Template name cannot be empty.");
            if (name.Length > MaxNameLength) return ValueResult<Guid>.FromError($"Template name cannot exceed {MaxNameLength} characters.");
            if (rows is null || rows.Count == 0) return ValueResult<Guid>.FromError("Template must have at least one row.");
            if (!IsHost(state, caller)) return ValueResult<Guid>.FromError("Only the host may save attribute templates.");

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.Name)) return ValueResult<Guid>.FromError("Row name cannot be empty.");
                if (!seen.Add(row.Name.Trim())) return ValueResult<Guid>.FromError($"Duplicate row name '{row.Name}'.");
            }

            var trimmed = name.Trim();
            var newId = Guid.NewGuid();
            string? error = null;
            var exec = state.Execute(() =>
            {
                if (state.CustomTemplates.Values.Any(t =>
                        string.Equals(t.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
                {
                    error = "A template with that name already exists.";
                    return;
                }

                state.CustomTemplates = state.CustomTemplates.SetItem(newId, new NamedTemplate
                {
                    Id = newId,
                    Name = trimmed,
                    Rows = [.. rows],
                });
            });

            if (exec.IsCanceled) return ValueResult<Guid>.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return ValueResult<Guid>.FromError(execErr);
            if (error is not null) return ValueResult<Guid>.FromError(error);
            return ValueResult<Guid>.FromValue(newId);
        }

        public Result DeleteCustomTemplateAsync(DndMapperGameState state, User caller, Guid templateId)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may delete attribute templates.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (!state.CustomTemplates.TryGetValue(templateId, out var existing)) { error = "Unknown template id."; return; }
                if (existing.IsBuiltIn) { error = "Built-in templates cannot be deleted."; return; }

                // Effect templates ride along on the NamedTemplate, so removing
                // it from the dictionary cascade-removes its library.
                state.CustomTemplates = state.CustomTemplates.Remove(templateId);

                // If the host just deleted the schema they were standing on,
                // snap the session back to DnD 5e core (a built-in, so always
                // present) and rebuild every sheet under that schema.
                if (state.ActiveSchemaTemplateId == templateId)
                {
                    var fallback = AttributeSchema.FromPreset(AttributePreset.DnD5eCore);
                    state.SetAttributeSchema(fallback);
                    state.SetActiveSchemaTemplateId(DndMapperGameState.BuiltInDnD5eCoreId);
                    state.SetInitiativeAttributeName(
                        state.CustomTemplates[DndMapperGameState.BuiltInDnD5eCoreId].InitiativeAttributeName);
                    state.Sheets = state.Sheets.ToImmutableDictionary(
                        kv => kv.Key,
                        kv => RemapSheetValues(kv.Value, fallback));
                }
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public Result UpdateCustomTemplateAsync(DndMapperGameState state, User caller, Guid templateId, IReadOnlyList<AttributeRow> rows)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (rows is null || rows.Count == 0) return Result.FromError("Template must have at least one row.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may edit attribute templates.");

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.Name)) return Result.FromError("Row name cannot be empty.");
                if (!seen.Add(row.Name.Trim())) return Result.FromError($"Duplicate row name '{row.Name}'.");
            }

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (!state.CustomTemplates.TryGetValue(templateId, out var existing)) { error = "Unknown template id."; return; }
                if (existing.IsBuiltIn) { error = "Built-in templates cannot be edited."; return; }
                state.CustomTemplates = state.CustomTemplates.SetItem(
                    templateId,
                    existing with { Rows = [.. rows] });
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public Result RenameCustomTemplateAsync(DndMapperGameState state, User caller, Guid templateId, string newName)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (string.IsNullOrWhiteSpace(newName)) return Result.FromError("Template name cannot be empty.");
            if (newName.Length > MaxNameLength) return Result.FromError($"Template name cannot exceed {MaxNameLength} characters.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may rename attribute templates.");

            var trimmed = newName.Trim();
            string? error = null;
            var exec = state.Execute(() =>
            {
                if (!state.CustomTemplates.TryGetValue(templateId, out var existing)) { error = "Unknown template id."; return; }
                if (existing.IsBuiltIn) { error = "Built-in templates cannot be renamed."; return; }
                if (state.CustomTemplates.Values.Any(t => t.Id != templateId &&
                        string.Equals(t.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
                {
                    error = "A template with that name already exists.";
                    return;
                }
                state.CustomTemplates = state.CustomTemplates.SetItem(
                    templateId,
                    existing with { Name = trimmed });
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public Result ApplyCustomTemplateAsync(DndMapperGameState state, User caller, Guid templateId)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may apply attribute templates.");

            if (!state.CustomTemplates.TryGetValue(templateId, out var template))
                return Result.FromError("Unknown template id.");

            // Built-in templates have a real preset; user templates land on Custom.
            // Either way, ChangeSchemaAsync gets the source id so the active
            // schema's status-effect library follows the schema swap.
            var preset = template.IsBuiltIn
                ? PresetForBuiltInTemplateId(templateId)
                : AttributePreset.Custom;
            var schema = new AttributeSchema(preset, [.. template.Rows]);
            return ChangeSchemaAsync(state, caller, schema, templateId);
        }

        private static AttributePreset PresetForBuiltInTemplateId(Guid id) =>
            id == DndMapperGameState.BuiltInDnD5eCoreId ? AttributePreset.DnD5eCore
            : id == DndMapperGameState.BuiltInDnD5ePlusSkillsId ? AttributePreset.DnD5ePlusCommonSkills
            : id == DndMapperGameState.BuiltInSimpleD20Id ? AttributePreset.SimpleD20
            : AttributePreset.Custom;

        // ── Settings verb ─────────────────────────────────────────────────────────

        public Result UpdateSettingsAsync(DndMapperGameState state, User caller, DndMapperSettings newSettings)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (newSettings is null) return Result.FromError("Settings are required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may update session settings.");

            return state.UpdateSettings(_ => newSettings with { });
        }

        // ── Loaded-dice verbs ─────────────────────────────────────────────────────

        public ValueResult<Guid> AddLoadedDiceRuleAsync(DndMapperGameState state, User caller, LoadedDiceRule rule)
        {
            if (state is null) return ValueResult<Guid>.FromError("State is required.");
            if (caller is null) return ValueResult<Guid>.FromError("Caller is required.");
            if (rule is null) return ValueResult<Guid>.FromError("Rule is required.");
            if (!IsHost(state, caller)) return ValueResult<Guid>.FromError("Only the host may edit loaded-dice rules.");

            var newId = rule.Id == Guid.Empty ? Guid.NewGuid() : rule.Id;
            var inserted = rule with { Id = newId };
            var exec = state.Execute(() =>
                state.SetLoadedDiceRules(state.LoadedDiceRules.Add(inserted)));

            if (exec.IsCanceled) return ValueResult<Guid>.FromCancellation();
            if (exec.TryGetFailure(out var err)) return ValueResult<Guid>.FromError(err);
            return ValueResult<Guid>.FromValue(newId);
        }

        public Result UpdateLoadedDiceRuleAsync(DndMapperGameState state, User caller, LoadedDiceRule rule)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (rule is null) return Result.FromError("Rule is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may edit loaded-dice rules.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                int idx = IndexOfRule(state, rule.Id);
                if (idx < 0) { error = "Unknown rule id."; return; }
                state.SetLoadedDiceRules(state.LoadedDiceRules.SetItem(idx, rule));
            });
            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public Result RemoveLoadedDiceRuleAsync(DndMapperGameState state, User caller, Guid ruleId)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may edit loaded-dice rules.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                int idx = IndexOfRule(state, ruleId);
                if (idx < 0) { error = "Unknown rule id."; return; }
                state.SetLoadedDiceRules(state.LoadedDiceRules.RemoveAt(idx));
            });
            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public Result MoveLoadedDiceRuleAsync(DndMapperGameState state, User caller, Guid ruleId, int newIndex)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may reorder loaded-dice rules.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                var rules = state.LoadedDiceRules;
                int idx = IndexOfRule(state, ruleId);
                if (idx < 0) { error = "Unknown rule id."; return; }
                int clamped = Math.Clamp(newIndex, 0, rules.Length - 1);
                if (clamped == idx) return;
                var rule = rules[idx];
                state.SetLoadedDiceRules(rules.RemoveAt(idx).Insert(clamped, rule));
            });
            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        // Host's client streams its held-key set so HostKeyHeldCondition can
        // see it at roll time. Silently ignores non-host callers (the client
        // attaches the listener only on the host page, but a malicious
        // caller could still craft the verb directly).
        public Result UpdateHostInputStateAsync(DndMapperGameState state, User caller, IReadOnlyCollection<string> heldKeys)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.Success;
            heldKeys ??= Array.Empty<string>();
            var set = ImmutableHashSet.CreateRange(StringComparer.Ordinal, heldKeys);
            var exec = state.Execute(() => state.SetHostHeldKeys(set));
            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var err)) return Result.FromError(err);
            return Result.Success;
        }

        private static int IndexOfRule(DndMapperGameState state, Guid ruleId)
        {
            for (int i = 0; i < state.LoadedDiceRules.Length; i++)
                if (state.LoadedDiceRules[i].Id == ruleId) return i;
            return -1;
        }

        // Shared helper used by RollAsync, ForceInitiativeRollAsync, and the
        // NPC initiative back-solve so every dice-producing path applies the
        // same loaded-dice pipeline. Returns the (deduplicated) list of
        // rules that actually fired, or an empty list when the master toggle
        // is off — letting the caller stamp the result without branching.
        private ImmutableArray<LoadedDiceRuleStamp> ApplyLoadedDiceToRolls(
            DndMapperGameState state,
            User caller,
            RollRequest request,
            Guid? rollerSheetId,
            IList<DieRoll> rolls)
        {
            if (!state.Settings.LoadedDiceEnabled) return ImmutableArray<LoadedDiceRuleStamp>.Empty;
            if (state.LoadedDiceRules.Length == 0) return ImmutableArray<LoadedDiceRuleStamp>.Empty;

            return LoadedDiceProcessor.Apply(
                rolls,
                state.LoadedDiceRules,
                rollerSheetId,
                sides => new LoadedDiceContext
                {
                    Caller = caller,
                    State = state,
                    Request = request,
                    RollerSheetId = rollerSheetId,
                    DiceTermSides = sides,
                    HostHeldKeys = state.HostHeldKeys,
                    RollNewDie = s => _rng.GetRandomInt(1, s + 1, RandomType.Fast),
                });
        }

        // ── Centre-viewport broadcast (v1.x — §6.4) ───────────────────────────────

        public Result RequestCenterViewportAsync(DndMapperGameState state, User caller, Guid mapId, double x, double y)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may centre everyone's viewport.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (!state.Maps.Any(m => m.Id == mapId)) { error = "Unknown map id."; return; }
                state.SetPendingCenterRequest(new CenterViewportRequest(mapId, x, y, Guid.NewGuid()));
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        // ── Focus-box viewport (display zoom-to-region) ───────────────────────────

        // Minimum rect dimension after clamping. Below this the display would
        // be zoomed in so far that any tiny coordinate jitter would scroll the
        // view wildly; also acts as the "zero-area" guard.
        private const double MinFocusRectSize = 0.25;

        public Result SetFocusRect(DndMapperGameState state, User caller, Guid mapId, double x, double y, double width, double height)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may set the focus box.");
            if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height))
                return Result.FromError("Focus box coordinates must be finite numbers.");
            if (width <= 0 || height <= 0)
                return Result.FromError("Focus box must have positive width and height.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                var map = state.Maps.FirstOrDefault(m => m.Id == mapId);
                if (map is null) { error = "Unknown map id."; return; }

                // Clamp the rectangle into the map's cell bounds. A rect drawn
                // partly off-canvas gets cropped to the on-canvas portion; a
                // rect entirely outside collapses below MinFocusRectSize and
                // is rejected below.
                double mapW = map.Grid.WidthCells;
                double mapH = map.Grid.HeightCells;

                double x0 = Math.Clamp(x, 0, mapW);
                double y0 = Math.Clamp(y, 0, mapH);
                double x1 = Math.Clamp(x + width, 0, mapW);
                double y1 = Math.Clamp(y + height, 0, mapH);

                double w = x1 - x0;
                double h = y1 - y0;
                if (w < MinFocusRectSize || h < MinFocusRectSize)
                {
                    error = "Focus box is too small or outside the map.";
                    return;
                }

                state.SetFocusRect(new FocusRect(mapId, x0, y0, w, h));
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public Result ClearFocusRect(DndMapperGameState state, User caller)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may clear the focus box.");

            var exec = state.Execute(() => state.SetFocusRect(null));

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var err)) return Result.FromError(err);
            return Result.Success;
        }

        // ── Initiative tracker (v1.x — §9.5) ──────────────────────────────────────

        public Result StartInitiativeAsync(DndMapperGameState state, User caller, IReadOnlyList<Guid>? npcTokenIds)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may start initiative.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (state.ActiveCombat is not null) { error = "Combat is already active."; return; }

                var turnBuilder = ImmutableArray.CreateBuilder<CombatantEntry>();
                var seenTokenIds = new HashSet<Guid>();

                if (npcTokenIds is not null)
                {
                    foreach (var tokenId in npcTokenIds)
                    {
                        var (token, _) = FindTokenAndMap(state, tokenId);
                        if (token is null) { error = $"Unknown token id {tokenId}."; return; }
                        if (token.Type != TokenType.NPCToken) { error = "Only NPC tokens can be added to combat selection."; return; }
                        if (!seenTokenIds.Add(tokenId)) continue;
                        turnBuilder.Add(new CombatantEntry
                        {
                            Id = Guid.NewGuid(),
                            TokenId = tokenId,
                            Name = token.Name,
                            OwnerUserId = null,
                        });
                    }
                }

                foreach (var entry in state.Players)
                {
                    var token = state.Maps
                        .SelectMany(m => m.Tokens)
                        .FirstOrDefault(t => t.Type == TokenType.PlayerToken && t.OwnerUserId == entry.User.Id);
                    turnBuilder.Add(new CombatantEntry
                    {
                        Id = Guid.NewGuid(),
                        TokenId = token?.Id ?? Guid.Empty,
                        Name = token?.Name ?? entry.User.Name,
                        OwnerUserId = entry.User.Id,
                    });
                }

                if (turnBuilder.Count == 0) { error = "Cannot start initiative with no combatants."; return; }

                state.SetActiveCombat(new CombatState
                {
                    Phase = CombatPhase.WaitingForRolls,
                    RoundNumber = 1,
                    TurnOrder = turnBuilder.ToImmutable(),
                });
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public Result SubmitInitiativeRollAsync(DndMapperGameState state, User caller)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");

            int modifier = ResolveInitiativeModifier(state, caller.Id);
            int d20 = _rng.GetRandomInt(1, 21, RandomType.Fast);

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (state.ActiveCombat is null) { error = "No active combat."; return; }
                if (state.ActiveCombat.Phase != CombatPhase.WaitingForRolls) { error = "Initiative phase has ended."; return; }
                var combat = state.ActiveCombat;
                var idx = IndexOfCombatantByOwner(combat, caller.Id);
                if (idx < 0) { error = "You are not a combatant in this initiative."; return; }
                if (combat.TurnOrder[idx].InitiativeRoll is not null) return; // no-op

                var (modifiedFace, stamps) = ApplyLoadedDiceToInitiative(state, caller, FindPlayerSheetId(state, caller.Id), d20);
                int total = modifiedFace + modifier;
                state.SetActiveCombat(combat with
                {
                    TurnOrder = combat.TurnOrder.SetItem(idx, combat.TurnOrder[idx] with { InitiativeRoll = total }),
                });
                state.AppendRoll(BuildInitiativeRollResult(caller.Id, null, modifiedFace, modifier, total, appliedRules: stamps));
                TryTransitionToActive(state);
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public Result SetNpcInitiativeAsync(DndMapperGameState state, User caller, Guid combatantId, int roll)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may set NPC initiative.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (state.ActiveCombat is null) { error = "No active combat."; return; }
                if (state.ActiveCombat.Phase != CombatPhase.WaitingForRolls) { error = "Initiative phase has ended."; return; }
                var combat = state.ActiveCombat;
                var idx = IndexOfCombatantById(combat, combatantId);
                if (idx < 0) { error = "Unknown combatant id."; return; }
                var entry = combat.TurnOrder[idx];
                if (entry.OwnerUserId is not null) { error = "Use ForceInitiativeRollAsync for player combatants."; return; }

                // Park the host's typed value on PendingInitiative — InitiativeRoll
                // stays null so the displays continue to read "—" and no
                // RollResult is appended (no dice animate yet). This is the
                // beat the user wants preserved: the manual set should feel
                // like cocking the trigger, not pulling it.
                var staged = combat.TurnOrder.SetItem(idx, entry with { PendingInitiative = roll });

                // Trigger to fire all dice: every NPC has a value (pending or
                // final). At that point we commit every pending NPC in one
                // sweep so each token's dice spin up together — same batch
                // path the bulk "Roll All Unset NPCs" verb uses.
                if (!HasUnsetNpc(staged))
                {
                    staged = CommitPendingNpcInitiatives(state, staged, caller);
                }

                state.SetActiveCombat(combat with { TurnOrder = staged });
                TryTransitionToActive(state);
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public Result ForceInitiativeRollAsync(DndMapperGameState state, User caller, Guid combatantId)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may force initiative rolls.");

            int d20 = _rng.GetRandomInt(1, 21, RandomType.Fast);

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (state.ActiveCombat is null) { error = "No active combat."; return; }
                if (state.ActiveCombat.Phase != CombatPhase.WaitingForRolls) { error = "Initiative phase has ended."; return; }
                var combat = state.ActiveCombat;
                var idx = IndexOfCombatantById(combat, combatantId);
                if (idx < 0) { error = "Unknown combatant id."; return; }
                var entry = combat.TurnOrder[idx];
                if (entry.OwnerUserId is null) { error = "Force-roll is only valid for player combatants."; return; }
                if (entry.InitiativeRoll is not null) { error = "Player has already rolled."; return; }

                int modifier = ResolveInitiativeModifier(state, entry.OwnerUserId);
                var (modifiedFace, stamps) = ApplyLoadedDiceToInitiative(state, caller, FindPlayerSheetId(state, entry.OwnerUserId), d20);
                int total = modifiedFace + modifier;
                state.SetActiveCombat(combat with
                {
                    TurnOrder = combat.TurnOrder.SetItem(idx, entry with
                    {
                        InitiativeRoll = total,
                        IsForceRolled = true,
                    }),
                });
                state.AppendRoll(BuildInitiativeRollResult(entry.OwnerUserId, caller.Id, modifiedFace, modifier, total, appliedRules: stamps));
                TryTransitionToActive(state);
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        // Rolls initiative for every NPC combatant that hasn't yet been
        // committed, in a single Execute. NPCs split into two buckets:
        //   • PendingInitiative is set → host already typed a value via
        //     SetNpcInitiativeAsync; honor that value and back-solve the
        //     visible d20 face so the dice "happen to land" on it.
        //   • Truly unset → roll a real d20 + the sheet-resolved modifier.
        // Both paths append a RollResult keyed on the NPC's TokenId so the
        // per-token DiceBox instances animate side-by-side.
        public Result RollAllNpcInitiativeAsync(DndMapperGameState state, User caller)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may roll for NPCs.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (state.ActiveCombat is null) { error = "No active combat."; return; }
                if (state.ActiveCombat.Phase != CombatPhase.WaitingForRolls) { error = "Initiative phase has ended."; return; }

                var combat = state.ActiveCombat;
                var newTurnOrder = combat.TurnOrder;
                for (int i = 0; i < newTurnOrder.Length; i++)
                {
                    var entry = newTurnOrder[i];
                    if (entry.OwnerUserId is not null) continue;       // players
                    if (entry.InitiativeRoll is not null) continue;    // already committed

                    var sheet = FindSheetForToken(state, entry.TokenId);
                    int modifier = ResolveInitiativeModifierForSheet(state, sheet);

                    int face;
                    int initiativeValue;
                    ImmutableArray<LoadedDiceRuleStamp> stamps = ImmutableArray<LoadedDiceRuleStamp>.Empty;
                    if (entry.PendingInitiative is int pending)
                    {
                        // Manual override: the host's typed value wins the
                        // turn order; the visible die is back-solved from it.
                        // Loaded-dice rules do not fire here — the host
                        // already chose the value explicitly.
                        face = Math.Clamp(pending - modifier, 1, 20);
                        initiativeValue = pending;
                    }
                    else
                    {
                        // Truly unset — fresh d20, subject to loaded-dice rules.
                        int rawFace = _rng.GetRandomInt(1, 21, RandomType.Fast);
                        (face, stamps) = ApplyLoadedDiceToInitiative(state, caller, sheet?.Id, rawFace);
                        initiativeValue = face + modifier;
                    }

                    newTurnOrder = newTurnOrder.SetItem(i, entry with
                    {
                        InitiativeRoll = initiativeValue,
                        PendingInitiative = null,
                    });
                    state.AppendRoll(BuildInitiativeRollResult(
                        rollerUserId: caller.Id,
                        forcedByUserId: caller.Id,
                        d20: face,
                        dexModifier: modifier,
                        total: face + modifier,
                        tokenId: entry.TokenId,
                        appliedRules: stamps));
                }

                state.SetActiveCombat(combat with { TurnOrder = newTurnOrder });
                TryTransitionToActive(state);
            });
            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        // True when at least one NPC combatant has neither been rolled nor
        // had a value parked in PendingInitiative. The SetNpcInitiativeAsync
        // commit trigger keys off the negation: no NPC truly unset → flush
        // every pending value as one RollResult batch.
        private static bool HasUnsetNpc(System.Collections.Immutable.ImmutableArray<CombatantEntry> turnOrder)
        {
            for (int i = 0; i < turnOrder.Length; i++)
            {
                var entry = turnOrder[i];
                if (entry.OwnerUserId is not null) continue;
                if (entry.InitiativeRoll is not null) continue;
                if (entry.PendingInitiative is not null) continue;
                return true;
            }
            return false;
        }

        // Walks the turn order and commits every NPC that has a
        // PendingInitiative: writes the value into InitiativeRoll, clears
        // PendingInitiative, and appends a RollResult whose d20 face is the
        // back-solved value (clamped to [1, 20]) so dice-box can animate it.
        // Caller stays as both roller and forcedBy so the audit log says
        // "host pressed Set" rather than "an NPC rolled itself."
        private System.Collections.Immutable.ImmutableArray<CombatantEntry> CommitPendingNpcInitiatives(
            DndMapperGameState state,
            System.Collections.Immutable.ImmutableArray<CombatantEntry> turnOrder,
            User caller)
        {
            for (int i = 0; i < turnOrder.Length; i++)
            {
                var entry = turnOrder[i];
                if (entry.OwnerUserId is not null) continue;
                if (entry.InitiativeRoll is not null) continue;
                if (entry.PendingInitiative is not int pending) continue;

                var sheet = FindSheetForToken(state, entry.TokenId);
                int modifier = ResolveInitiativeModifierForSheet(state, sheet);
                int face = Math.Clamp(pending - modifier, 1, 20);

                turnOrder = turnOrder.SetItem(i, entry with
                {
                    InitiativeRoll = pending,
                    PendingInitiative = null,
                });
                state.AppendRoll(BuildInitiativeRollResult(
                    rollerUserId: caller.Id,
                    forcedByUserId: caller.Id,
                    d20: face,
                    dexModifier: modifier,
                    total: face + modifier,
                    tokenId: entry.TokenId));
            }
            return turnOrder;
        }

        private static CharacterSheet? FindSheetForToken(DndMapperGameState state, Guid tokenId)
        {
            foreach (var map in state.Maps)
            {
                foreach (var token in map.Tokens)
                {
                    if (token.Id != tokenId) continue;
                    if (token.SheetId is Guid sid && state.Sheets.TryGetValue(sid, out var sheet))
                        return sheet;
                    return null;
                }
            }
            return null;
        }

        public Result AdvanceTurnAsync(DndMapperGameState state, User caller)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may advance the turn.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (state.ActiveCombat is null) { error = "No active combat."; return; }
                if (state.ActiveCombat.Phase != CombatPhase.Active) { error = "Combat is not in the active phase."; return; }
                if (state.ActiveCombat.TurnOrder.Length == 0) { error = "Turn order is empty."; return; }

                var combat = state.ActiveCombat;
                var nextIdx = combat.CurrentTurnIndex + 1;
                var nextRound = combat.RoundNumber;
                if (nextIdx >= combat.TurnOrder.Length)
                {
                    nextIdx = 0;
                    nextRound = combat.RoundNumber + 1;
                }
                state.SetActiveCombat(combat with
                {
                    CurrentTurnIndex = nextIdx,
                    RoundNumber = nextRound,
                });
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public ValueResult<Guid> AddCombatantAsync(DndMapperGameState state, User caller, Guid tokenId, int initiativeRoll)
        {
            if (state is null) return ValueResult<Guid>.FromError("State is required.");
            if (caller is null) return ValueResult<Guid>.FromError("Caller is required.");
            if (!IsHost(state, caller)) return ValueResult<Guid>.FromError("Only the host may add combatants.");

            var newId = Guid.NewGuid();
            string? error = null;
            var exec = state.Execute(() =>
            {
                if (state.ActiveCombat is null) { error = "No active combat."; return; }
                var (token, _) = FindTokenAndMap(state, tokenId);
                if (token is null) { error = "Unknown token id."; return; }
                var combat = state.ActiveCombat;
                if (combat.TurnOrder.Any(e => e.TokenId == tokenId))
                {
                    error = "Token is already a combatant.";
                    return;
                }

                var entry = new CombatantEntry
                {
                    Id = newId,
                    TokenId = tokenId,
                    Name = token.Name,
                    OwnerUserId = token.Type == TokenType.PlayerToken ? token.OwnerUserId : null,
                    InitiativeRoll = initiativeRoll,
                };
                int insertIdx = TurnOrderSorter.FindInsertionIndex(combat.TurnOrder, entry);
                var newTurnOrder = combat.TurnOrder.Insert(insertIdx, entry);

                var nextCurrent = combat.CurrentTurnIndex;
                if (combat.Phase == CombatPhase.Active && insertIdx <= combat.CurrentTurnIndex)
                {
                    nextCurrent = combat.CurrentTurnIndex + 1;
                }

                state.SetActiveCombat(combat with
                {
                    TurnOrder = newTurnOrder,
                    CurrentTurnIndex = nextCurrent,
                });
            });

            if (exec.IsCanceled) return ValueResult<Guid>.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return ValueResult<Guid>.FromError(execErr);
            if (error is not null) return ValueResult<Guid>.FromError(error);
            return ValueResult<Guid>.FromValue(newId);
        }

        public Result RemoveCombatantAsync(DndMapperGameState state, User caller, Guid combatantId)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may remove combatants.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (state.ActiveCombat is null) { error = "No active combat."; return; }
                var combat = state.ActiveCombat;
                var idx = IndexOfCombatantById(combat, combatantId);
                if (idx < 0) { error = "Unknown combatant id."; return; }

                bool removingCurrent = combat.Phase == CombatPhase.Active && idx == combat.CurrentTurnIndex;
                var newTurnOrder = combat.TurnOrder.RemoveAt(idx);

                if (newTurnOrder.Length == 0)
                {
                    state.SetActiveCombat(null);
                    return;
                }

                var nextCurrent = combat.CurrentTurnIndex;
                var nextRound = combat.RoundNumber;
                if (combat.Phase == CombatPhase.Active)
                {
                    if (idx < combat.CurrentTurnIndex)
                    {
                        nextCurrent = combat.CurrentTurnIndex - 1;
                    }
                    else if (removingCurrent)
                    {
                        // Stay at idx (which now points to the next combatant), wrap if past end.
                        if (nextCurrent >= newTurnOrder.Length)
                        {
                            nextCurrent = 0;
                            nextRound = combat.RoundNumber + 1;
                        }
                    }
                }

                state.SetActiveCombat(combat with
                {
                    TurnOrder = newTurnOrder,
                    CurrentTurnIndex = nextCurrent,
                    RoundNumber = nextRound,
                });
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public Result EndCombatAsync(DndMapperGameState state, User caller)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may end combat.");

            var exec = state.Execute(() => state.SetActiveCombat(null));
            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return Result.Success;
        }

        private static void TryTransitionToActive(DndMapperGameState state)
        {
            if (state.ActiveCombat is null) return;
            if (state.ActiveCombat.Phase != CombatPhase.WaitingForRolls) return;
            if (state.ActiveCombat.TurnOrder.Any(e => e.InitiativeRoll is null)) return;

            var sorted = TurnOrderSorter.Sort(state.ActiveCombat.TurnOrder);
            state.SetActiveCombat(state.ActiveCombat with
            {
                TurnOrder = sorted.ToImmutableArray(),
                Phase = CombatPhase.Active,
                CurrentTurnIndex = 0,
            });
        }

        private static int IndexOfCombatantById(CombatState combat, Guid combatantId)
        {
            for (int i = 0; i < combat.TurnOrder.Length; i++)
                if (combat.TurnOrder[i].Id == combatantId) return i;
            return -1;
        }

        private static int IndexOfCombatantByOwner(CombatState combat, string ownerUserId)
        {
            for (int i = 0; i < combat.TurnOrder.Length; i++)
                if (combat.TurnOrder[i].OwnerUserId == ownerUserId) return i;
            return -1;
        }

        // Resolves the initiative modifier for the owner of a sheet. Reads
        // the state-level InitiativeAttributeName, falling back to a
        // case-insensitive "DEX" lookup so older saves keep working. Routes
        // through AttributeContributionResolver so active status effects
        // (e.g. "Slowed" with DEX −3) apply the same way they would to a
        // normal roll — §8.5 attribute-value semantics.
        private static int ResolveInitiativeModifier(DndMapperGameState state, string userId)
            => ResolveInitiativeModifierForSheet(state, state.Sheets.Values.FirstOrDefault(s => s.OwnerUserId == userId));

        private static int ResolveInitiativeModifierForSheet(DndMapperGameState state, CharacterSheet? sheet)
        {
            if (sheet is null) return 0;
            var configured = state.InitiativeAttributeName;
            if (!string.IsNullOrEmpty(configured)
                && sheet.Values.TryGetValue(configured, out var configuredValue))
            {
                return AttributeContributionResolver.Resolve(sheet, configured, configuredValue).EffectiveModifier;
            }

            // Legacy fallback: case-insensitive DEX search (older saves with
            // no state-level InitiativeAttributeName persisted).
            foreach (var (name, value) in sheet.Values)
            {
                if (!string.Equals(name, "DEX", StringComparison.OrdinalIgnoreCase)) continue;
                return AttributeContributionResolver.Resolve(sheet, name, value).EffectiveModifier;
            }
            return 0;
        }

        public Result SetInitiativeAttributeAsync(DndMapperGameState state, User caller, string? attributeName)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may change the initiative attribute.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (!string.IsNullOrEmpty(attributeName)
                    && !state.AttributeSchema.Rows.Any(r => r.Name == attributeName))
                {
                    error = "Unknown attribute name for the active schema.";
                    return;
                }
                state.SetInitiativeAttributeName(attributeName);
                // Mirror onto the active template (if any) so re-applying the
                // template later restores the host's choice.
                if (state.ActiveSchemaTemplateId is { } activeId
                    && state.CustomTemplates.TryGetValue(activeId, out var template))
                {
                    state.CustomTemplates = state.CustomTemplates.SetItem(
                        activeId,
                        template with { InitiativeAttributeName = string.IsNullOrEmpty(attributeName) ? null : attributeName });
                }
            });
            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        private static string FormatSigned(int value)
            => value >= 0 ? $"+{value}" : $"−{Math.Abs(value)}";

        private static RollResult BuildInitiativeRollResult(
            string rollerUserId,
            string? forcedByUserId,
            int d20,
            int dexModifier,
            int total,
            Guid? tokenId = null,
            ImmutableArray<LoadedDiceRuleStamp>? appliedRules = null)
        {
            return new RollResult(
                Id: Guid.NewGuid(),
                RollerUserId: rollerUserId,
                ForcedByUserId: forcedByUserId,
                Rolls: [new DieRoll(20, d20, false)],
                Total: total,
                Mode: RollMode.Normal,
                FlatModifier: 0,
                AttributeModifier: dexModifier,
                Label: RollResult.InitiativeLabel,
                TimestampUtc: DateTime.UtcNow,
                Formula: "1d20",
                ModifierBreakdown: null,
                TokenId: tokenId)
            { AppliedRules = appliedRules ?? ImmutableArray<LoadedDiceRuleStamp>.Empty };
        }

        // Initiative rolls don't carry a RollRequest; build a synthetic one
        // so DiceTypeRolledCondition, RollLabelContainsCondition, etc. can
        // still match. Returns the modified d20 face and the stamps for the
        // log. Called inside Execute so the state snapshot is stable.
        private (int Face, ImmutableArray<LoadedDiceRuleStamp> Stamps) ApplyLoadedDiceToInitiative(
            DndMapperGameState state,
            User caller,
            Guid? rollerSheetId,
            int rawD20)
        {
            if (!state.Settings.LoadedDiceEnabled || state.LoadedDiceRules.Length == 0)
                return (rawD20, ImmutableArray<LoadedDiceRuleStamp>.Empty);

            var rolls = new List<DieRoll> { new(20, rawD20, false) };
            var request = new RollRequest(
                Dice: [new DiceTerm(1, 20)],
                AttributeRef: null,
                FlatModifier: 0,
                Mode: RollMode.Normal,
                Label: RollResult.InitiativeLabel);

            var stamps = ApplyLoadedDiceToRolls(state, caller, request, rollerSheetId, rolls);
            return (rolls[0].Result, stamps);
        }

        // Player's primary character sheet — first sheet they own. Returns
        // null when the player has no sheet attached (e.g. host has not yet
        // generated one). Used to feed RollerSheetId for target-list matches.
        private static Guid? FindPlayerSheetId(DndMapperGameState state, string userId)
        {
            foreach (var sheet in state.Sheets.Values)
                if (sheet.OwnerUserId == userId) return sheet.Id;
            return null;
        }

        // ── Markup overlay (v1.x — §5.6) ──────────────────────────────────────────

        public Result UpdateMapMarkupAsync(DndMapperGameState state, User caller, Guid mapId, string? svgContent)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may update map markup.");

            // Sanitise outside the lock — XML parsing is non-trivial work.
            var sanitized = KnockBox.Core.Services.Drawing.SvgContentSanitizer.Sanitize(svgContent);
            if (string.IsNullOrWhiteSpace(sanitized)) sanitized = null;

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (!state.UpdateMap(mapId, m => m with { MarkupSvg = sanitized }))
                    error = "Unknown map id.";
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public Result ClearMapMarkupAsync(DndMapperGameState state, User caller, Guid mapId)
            => UpdateMapMarkupAsync(state, caller, mapId, null);

        // ── Status effects (v1.x — §8.5) ──────────────────────────────────────────

        public ValueResult<Guid> ApplyStatusEffectAsync(
            DndMapperGameState state,
            User caller,
            Guid sheetId,
            string name,
            IReadOnlyList<AttributeDelta>? attributeDeltas,
            int? maxHpDelta,
            int? onApplyHpDelta,
            string? notes)
        {
            if (state is null) return ValueResult<Guid>.FromError("State is required.");
            if (caller is null) return ValueResult<Guid>.FromError("Caller is required.");
            if (string.IsNullOrWhiteSpace(name)) return ValueResult<Guid>.FromError("Status effect name is required.");
            if (name.Length > MaxNameLength) return ValueResult<Guid>.FromError($"Status effect name cannot exceed {MaxNameLength} characters.");

            var newId = Guid.NewGuid();
            string? error = null;
            var exec = state.Execute(() =>
            {
                if (!state.Sheets.TryGetValue(sheetId, out var sheet)) { error = "Unknown sheet id."; return; }
                if (!CanEditSheet(state, caller, sheet)) { error = "You are not permitted to edit this sheet."; return; }

                var effect = new StatusEffect
                {
                    Id = newId,
                    Name = name,
                    AttributeDeltas = attributeDeltas is null ? ImmutableArray<AttributeDelta>.Empty : [.. attributeDeltas],
                    MaxHpDelta = maxHpDelta,
                    OnApplyHpDelta = onApplyHpDelta,
                    Notes = notes ?? string.Empty,
                    AppliedUtc = DateTime.UtcNow,
                };
                state.UpdateSheet(sheetId, s =>
                {
                    var updated = s with { StatusEffects = s.StatusEffects.Add(effect) };
                    if (updated.Hp is int hp && onApplyHpDelta is int delta)
                        updated = updated with { Hp = hp + delta };
                    return ClampHpToEffectiveMax(updated);
                });
            });

            if (exec.IsCanceled) return ValueResult<Guid>.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return ValueResult<Guid>.FromError(execErr);
            if (error is not null) return ValueResult<Guid>.FromError(error);
            return ValueResult<Guid>.FromValue(newId);
        }

        public Result UpdateStatusEffectAsync(
            DndMapperGameState state,
            User caller,
            Guid sheetId,
            Guid effectId,
            string name,
            IReadOnlyList<AttributeDelta>? attributeDeltas,
            int? maxHpDelta,
            int? onApplyHpDelta,
            string? notes)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (string.IsNullOrWhiteSpace(name)) return Result.FromError("Status effect name is required.");
            if (name.Length > MaxNameLength) return Result.FromError($"Status effect name cannot exceed {MaxNameLength} characters.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (!state.Sheets.TryGetValue(sheetId, out var sheet)) { error = "Unknown sheet id."; return; }
                if (!CanEditSheet(state, caller, sheet)) { error = "You are not permitted to edit this sheet."; return; }
                var idx = IndexOf(sheet.StatusEffects, effectId);
                if (idx < 0) { error = "Unknown status effect id."; return; }

                state.UpdateSheet(sheetId, s =>
                {
                    var newList = s.StatusEffects.SetItem(idx, s.StatusEffects[idx] with
                    {
                        Name = name,
                        AttributeDeltas = attributeDeltas is null ? ImmutableArray<AttributeDelta>.Empty : [.. attributeDeltas],
                        MaxHpDelta = maxHpDelta,
                        OnApplyHpDelta = onApplyHpDelta,
                        Notes = notes ?? string.Empty,
                    });
                    return ClampHpToEffectiveMax(s with { StatusEffects = newList });
                });
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public Result RemoveStatusEffectAsync(DndMapperGameState state, User caller, Guid sheetId, Guid effectId)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (!state.Sheets.TryGetValue(sheetId, out var sheet)) { error = "Unknown sheet id."; return; }
                if (!CanEditSheet(state, caller, sheet)) { error = "You are not permitted to edit this sheet."; return; }
                var idx = IndexOf(sheet.StatusEffects, effectId);
                if (idx < 0) { error = "Unknown status effect id."; return; }
                state.UpdateSheet(sheetId, s => ClampHpToEffectiveMax(s with { StatusEffects = s.StatusEffects.RemoveAt(idx) }));
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public ValueResult<Guid> CreateStatusEffectTemplateAsync(
            DndMapperGameState state,
            User caller,
            string name,
            IReadOnlyList<AttributeDelta>? attributeDeltas,
            int? maxHpDelta,
            int? onApplyHpDelta,
            string? notes)
        {
            if (state is null) return ValueResult<Guid>.FromError("State is required.");
            if (caller is null) return ValueResult<Guid>.FromError("Caller is required.");
            if (string.IsNullOrWhiteSpace(name)) return ValueResult<Guid>.FromError("Template name is required.");
            if (name.Length > MaxNameLength) return ValueResult<Guid>.FromError($"Template name cannot exceed {MaxNameLength} characters.");
            if (!IsHost(state, caller)) return ValueResult<Guid>.FromError("Only the host may manage status effect templates.");

            var newId = Guid.NewGuid();
            string? error = null;
            var exec = state.Execute(() =>
            {
                var schema = state.GetActiveSchemaTemplate();
                if (schema is null)
                {
                    error = "Save the current schema as a template before authoring status effects.";
                    return;
                }
                state.CustomTemplates = state.CustomTemplates.SetItem(
                    schema.Id,
                    schema with
                    {
                        StatusEffectTemplates = schema.StatusEffectTemplates.Add(new StatusEffectTemplate
                        {
                            Id = newId,
                            Name = name,
                            AttributeDeltas = attributeDeltas is null ? ImmutableArray<AttributeDelta>.Empty : [.. attributeDeltas],
                            MaxHpDelta = maxHpDelta,
                            OnApplyHpDelta = onApplyHpDelta,
                            Notes = notes ?? string.Empty,
                        }),
                    });
            });

            if (exec.IsCanceled) return ValueResult<Guid>.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return ValueResult<Guid>.FromError(execErr);
            if (error is not null) return ValueResult<Guid>.FromError(error);
            return ValueResult<Guid>.FromValue(newId);
        }

        public Result UpdateStatusEffectTemplateAsync(
            DndMapperGameState state,
            User caller,
            Guid templateId,
            string name,
            IReadOnlyList<AttributeDelta>? attributeDeltas,
            int? maxHpDelta,
            int? onApplyHpDelta,
            string? notes)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (string.IsNullOrWhiteSpace(name)) return Result.FromError("Template name is required.");
            if (name.Length > MaxNameLength) return Result.FromError($"Template name cannot exceed {MaxNameLength} characters.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may manage status effect templates.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                var schema = state.GetActiveSchemaTemplate();
                if (schema is null) { error = "No active schema."; return; }
                var idx = IndexOf(schema.StatusEffectTemplates, templateId);
                if (idx < 0) { error = "Unknown template id."; return; }

                state.CustomTemplates = state.CustomTemplates.SetItem(
                    schema.Id,
                    schema with
                    {
                        StatusEffectTemplates = schema.StatusEffectTemplates.SetItem(idx, schema.StatusEffectTemplates[idx] with
                        {
                            Name = name,
                            AttributeDeltas = attributeDeltas is null ? ImmutableArray<AttributeDelta>.Empty : [.. attributeDeltas],
                            MaxHpDelta = maxHpDelta,
                            OnApplyHpDelta = onApplyHpDelta,
                            Notes = notes ?? string.Empty,
                        }),
                    });
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public Result DeleteStatusEffectTemplateAsync(DndMapperGameState state, User caller, Guid templateId)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may manage status effect templates.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                var schema = state.GetActiveSchemaTemplate();
                if (schema is null) { error = "No active schema."; return; }
                var idx = IndexOf(schema.StatusEffectTemplates, templateId);
                if (idx < 0) { error = "Unknown template id."; return; }
                state.CustomTemplates = state.CustomTemplates.SetItem(
                    schema.Id,
                    schema with { StatusEffectTemplates = schema.StatusEffectTemplates.RemoveAt(idx) });
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        private static CharacterSheet ClampHpToEffectiveMax(CharacterSheet sheet)
        {
            var effectiveMax = EffectiveMaxHpResolver.ResolveEffectiveMaxHp(sheet);
            if (effectiveMax is int max && sheet.Hp is int hp && hp > max)
                return sheet with { Hp = max };
            return sheet;
        }

        private static int IndexOf(ImmutableArray<StatusEffect> list, Guid id)
        {
            for (int i = 0; i < list.Length; i++)
                if (list[i].Id == id) return i;
            return -1;
        }

        private static int IndexOf(ImmutableArray<StatusEffectTemplate> list, Guid id)
        {
            for (int i = 0; i < list.Length; i++)
                if (list[i].Id == id) return i;
            return -1;
        }

        private static int IndexOf(ImmutableArray<RollTemplate> list, Guid id)
        {
            for (int i = 0; i < list.Length; i++)
                if (list[i].Id == id) return i;
            return -1;
        }

        // ── Roll template verbs ───────────────────────────────────────────────────
        //
        // Three tiers exist for roll templates:
        //   - Built-in: ships with the plugin, static, never edited.
        //   - Global: lives on DndMapperGameState, host-managed, visible to every sheet.
        //   - Sheet: lives on CharacterSheet, the sheet owner OR host can manage.
        //
        // Roll templates store an optional AttributeName that is *not* mutated on
        // schema changes — at apply time the panel resolves missing names to
        // "unselected" so the binding restores when the schema returns.

        private static string? ValidateRollDice(
            IReadOnlyList<DiceTerm>? dice, RollMode mode)
        {
            if (dice is null || dice.Count == 0) return "At least one dice term is required.";

            int totalDice = 0;
            foreach (var term in dice)
            {
                if (term.Count < 1) return "Each dice term must roll at least one die.";
                if (Array.IndexOf(AllowedDieSides, term.Sides) < 0)
                    return $"Unsupported die size d{term.Sides}.";
                totalDice += term.Count;
            }
            if (totalDice > MaxRollDiceCount)
                return "Cannot roll more than 20 dice in a single request.";

            if (mode != RollMode.Normal && (dice.Count != 1 || dice[0].Count != 1))
                return "Advantage/Disadvantage requires exactly one die.";

            return null;
        }

        public ValueResult<Guid> CreateGlobalRollTemplateAsync(
            DndMapperGameState state, User caller, string name,
            IReadOnlyList<DiceTerm> dice, int flatModifier, RollMode mode,
            string? attributeName, string? label)
        {
            if (state is null) return ValueResult<Guid>.FromError("State is required.");
            if (caller is null) return ValueResult<Guid>.FromError("Caller is required.");
            if (string.IsNullOrWhiteSpace(name)) return ValueResult<Guid>.FromError("Template name is required.");
            if (name.Length > MaxNameLength) return ValueResult<Guid>.FromError($"Template name cannot exceed {MaxNameLength} characters.");
            if (!IsHost(state, caller)) return ValueResult<Guid>.FromError("Only the host may manage global roll templates.");
            if (ValidateRollDice(dice, mode) is string diceErr) return ValueResult<Guid>.FromError(diceErr);

            var newId = Guid.NewGuid();
            var template = new RollTemplate(
                newId, name, [.. dice], flatModifier, mode,
                string.IsNullOrWhiteSpace(attributeName) ? null : attributeName,
                label ?? string.Empty, RollTemplateScope.Global);

            var exec = state.Execute(() => state.GlobalRollTemplates = state.GlobalRollTemplates.Add(template));
            if (exec.IsCanceled) return ValueResult<Guid>.FromCancellation();
            if (exec.TryGetFailure(out var err)) return ValueResult<Guid>.FromError(err);
            return ValueResult<Guid>.FromValue(newId);
        }

        public Result UpdateGlobalRollTemplateAsync(
            DndMapperGameState state, User caller, Guid templateId, string name,
            IReadOnlyList<DiceTerm> dice, int flatModifier, RollMode mode,
            string? attributeName, string? label)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (string.IsNullOrWhiteSpace(name)) return Result.FromError("Template name is required.");
            if (name.Length > MaxNameLength) return Result.FromError($"Template name cannot exceed {MaxNameLength} characters.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may manage global roll templates.");
            if (DndMapperGameState.IsBuiltInRollTemplateId(templateId))
                return Result.FromError("Built-in roll templates cannot be edited.");
            if (ValidateRollDice(dice, mode) is string diceErr) return Result.FromError(diceErr);

            string? error = null;
            var exec = state.Execute(() =>
            {
                int idx = IndexOf(state.GlobalRollTemplates, templateId);
                if (idx < 0) { error = "Unknown template id."; return; }
                state.GlobalRollTemplates = state.GlobalRollTemplates.SetItem(idx, new RollTemplate(
                    templateId, name, [.. dice], flatModifier, mode,
                    string.IsNullOrWhiteSpace(attributeName) ? null : attributeName,
                    label ?? string.Empty, RollTemplateScope.Global));
            });
            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public Result DeleteGlobalRollTemplateAsync(DndMapperGameState state, User caller, Guid templateId)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may manage global roll templates.");
            if (DndMapperGameState.IsBuiltInRollTemplateId(templateId))
                return Result.FromError("Built-in roll templates cannot be deleted.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                int idx = IndexOf(state.GlobalRollTemplates, templateId);
                if (idx < 0) { error = "Unknown template id."; return; }
                state.GlobalRollTemplates = state.GlobalRollTemplates.RemoveAt(idx);
            });
            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public ValueResult<Guid> CreateSheetRollTemplateAsync(
            DndMapperGameState state, User caller, Guid sheetId, string name,
            IReadOnlyList<DiceTerm> dice, int flatModifier, RollMode mode,
            string? attributeName, string? label)
        {
            if (state is null) return ValueResult<Guid>.FromError("State is required.");
            if (caller is null) return ValueResult<Guid>.FromError("Caller is required.");
            if (string.IsNullOrWhiteSpace(name)) return ValueResult<Guid>.FromError("Template name is required.");
            if (name.Length > MaxNameLength) return ValueResult<Guid>.FromError($"Template name cannot exceed {MaxNameLength} characters.");
            if (ValidateRollDice(dice, mode) is string diceErr) return ValueResult<Guid>.FromError(diceErr);

            if (!state.Sheets.TryGetValue(sheetId, out var sheet))
                return ValueResult<Guid>.FromError("Unknown sheet id.");
            if (!IsHost(state, caller) && sheet.OwnerUserId != caller.Id)
                return ValueResult<Guid>.FromError("You may only manage roll templates on your own sheet.");

            var newId = Guid.NewGuid();
            var template = new RollTemplate(
                newId, name, [.. dice], flatModifier, mode,
                string.IsNullOrWhiteSpace(attributeName) ? null : attributeName,
                label ?? string.Empty, RollTemplateScope.Sheet);

            var exec = state.Execute(() => state.UpdateSheet(sheetId, s => s with { RollTemplates = s.RollTemplates.Add(template) }));
            if (exec.IsCanceled) return ValueResult<Guid>.FromCancellation();
            if (exec.TryGetFailure(out var err)) return ValueResult<Guid>.FromError(err);
            return ValueResult<Guid>.FromValue(newId);
        }

        public Result UpdateSheetRollTemplateAsync(
            DndMapperGameState state, User caller, Guid sheetId, Guid templateId, string name,
            IReadOnlyList<DiceTerm> dice, int flatModifier, RollMode mode,
            string? attributeName, string? label)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (string.IsNullOrWhiteSpace(name)) return Result.FromError("Template name is required.");
            if (name.Length > MaxNameLength) return Result.FromError($"Template name cannot exceed {MaxNameLength} characters.");
            if (DndMapperGameState.IsBuiltInRollTemplateId(templateId))
                return Result.FromError("Built-in roll templates cannot be edited.");
            if (ValidateRollDice(dice, mode) is string diceErr) return Result.FromError(diceErr);

            if (!state.Sheets.TryGetValue(sheetId, out var sheet))
                return Result.FromError("Unknown sheet id.");
            if (!IsHost(state, caller) && sheet.OwnerUserId != caller.Id)
                return Result.FromError("You may only manage roll templates on your own sheet.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (!state.Sheets.TryGetValue(sheetId, out var s)) { error = "Unknown sheet id."; return; }
                int idx = IndexOf(s.RollTemplates, templateId);
                if (idx < 0) { error = "Unknown template id."; return; }
                state.UpdateSheet(sheetId, x => x with
                {
                    RollTemplates = x.RollTemplates.SetItem(idx, new RollTemplate(
                        templateId, name, [.. dice], flatModifier, mode,
                        string.IsNullOrWhiteSpace(attributeName) ? null : attributeName,
                        label ?? string.Empty, RollTemplateScope.Sheet)),
                });
            });
            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public Result DeleteSheetRollTemplateAsync(
            DndMapperGameState state, User caller, Guid sheetId, Guid templateId)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (DndMapperGameState.IsBuiltInRollTemplateId(templateId))
                return Result.FromError("Built-in roll templates cannot be deleted.");

            if (!state.Sheets.TryGetValue(sheetId, out var sheet))
                return Result.FromError("Unknown sheet id.");
            if (!IsHost(state, caller) && sheet.OwnerUserId != caller.Id)
                return Result.FromError("You may only manage roll templates on your own sheet.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (!state.Sheets.TryGetValue(sheetId, out var s)) { error = "Unknown sheet id."; return; }
                int idx = IndexOf(s.RollTemplates, templateId);
                if (idx < 0) { error = "Unknown template id."; return; }
                state.UpdateSheet(sheetId, x => x with { RollTemplates = x.RollTemplates.RemoveAt(idx) });
            });
            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        // ── Dice verb ─────────────────────────────────────────────────────────────

        public ValueResult<RollResult> RollAsync(DndMapperGameState state, User caller, RollRequest request)
        {
            if (state is null) return ValueResult<RollResult>.FromError("State is required.");
            if (caller is null) return ValueResult<RollResult>.FromError("Caller is required.");
            if (request is null) return ValueResult<RollResult>.FromError("Roll request is required.");
            if (request.Dice is null || request.Dice.Count == 0)
                return ValueResult<RollResult>.FromError("At least one dice term is required.");

            int totalDice = 0;
            foreach (var term in request.Dice)
            {
                if (term.Count < 1) return ValueResult<RollResult>.FromError("Each dice term must roll at least one die.");
                if (Array.IndexOf(AllowedDieSides, term.Sides) < 0)
                    return ValueResult<RollResult>.FromError($"Unsupported die size d{term.Sides}.");
                totalDice += term.Count;
            }
            if (totalDice > MaxRollDiceCount)
                return ValueResult<RollResult>.FromError("Cannot roll more than 20 dice in a single request.");

            if (request.Mode != RollMode.Normal)
            {
                // Adv/Dis applies to any single-die roll — d20 is the common case,
                // but coin-flip-style mechanics on smaller/larger dice are legal too.
                if (request.Dice.Count != 1 || request.Dice[0].Count != 1)
                    return ValueResult<RollResult>.FromError("Advantage/Disadvantage requires exactly one die.");
            }

            int? attributeModifier = null;
            AttributeContribution? contribution = null;
            AttributeValue? baseAttrValue = null;
            string? attrName = null;
            // AttributeRef with a null/empty AttributeName means "rolling as
            // this sheet, no attribute mod applied" — used by the host's
            // picker when they don't pick an attribute. The sheet identity is
            // still consumed by loaded-dice rule matching below, so ownership
            // MUST be validated whenever a SheetId is present (not only when
            // an attribute name is supplied) — otherwise a crafted verb could
            // claim another player's sheet to spoof loaded-dice attribution.
            if (request.AttributeRef is { } attrRef)
            {
                if (!state.Sheets.TryGetValue(attrRef.SheetId, out var sheet))
                    return ValueResult<RollResult>.FromError("Unknown sheet id for attribute reference.");
                if (sheet.OwnerUserId is not null && sheet.OwnerUserId != caller.Id && !IsHost(state, caller))
                    return ValueResult<RollResult>.FromError("You may only reference your own attributes (or be host).");

                // Attribute modifier only resolves when a name is supplied;
                // a name-less ref is "rolling as this sheet" with no mod.
                if (!string.IsNullOrEmpty(attrRef.AttributeName))
                {
                    if (!sheet.Values.TryGetValue(attrRef.AttributeName, out var attrValue))
                        return ValueResult<RollResult>.FromError("Unknown attribute name on the referenced sheet.");

                    if (attrValue.GetModifier() is null)
                        return ValueResult<RollResult>.FromError("Referenced attribute does not produce a numeric modifier.");

                    // §8.5 semantics: deltas apply to the attribute *value*; the
                    // scoring mode then converts the modified value to a roll
                    // modifier. The resolver handles both Score (re-floor after
                    // delta) and Modifier (straight pass-through) types.
                    var resolved = AttributeContributionResolver.Resolve(sheet, attrRef.AttributeName, attrValue);
                    attributeModifier = resolved.EffectiveModifier;
                    baseAttrValue = attrValue;
                    attrName = attrRef.AttributeName;
                    if (resolved.ValueBreakdown.Count > 1) contribution = resolved;
                }
            }

            var rolls = new List<DieRoll>(totalDice + (request.Mode == RollMode.Normal ? 0 : 1));
            foreach (var term in request.Dice)
            {
                for (int i = 0; i < term.Count; i++)
                {
                    int roll = _rng.GetRandomInt(1, term.Sides + 1, RandomType.Fast);
                    rolls.Add(new DieRoll(term.Sides, roll, false));
                }
            }

            if (request.Mode != RollMode.Normal)
            {
                // The guard above ensures Dice has exactly one term with Count==1,
                // so the second die uses the same Sides as the first.
                int sides = request.Dice[0].Sides;
                int second = _rng.GetRandomInt(1, sides + 1, RandomType.Fast);
                rolls.Add(new DieRoll(sides, second, false));
            }

            // Loaded-dice rules fire AFTER all RNG (including the advantage
            // twin) but BEFORE the discard step, so a rule that says
            // "make d20 = 20" turns both Advantage candidates into 20 and
            // the keep-highest logic still produces a 20 — symmetric with
            // disadvantage producing a 1 under "make d20 = 1".
            //
            // AttributeRef.SheetId is the sole sheet-identity source: the
            // submitter sets it from the host's "From sheet" picker (or a
            // player's assigned sheet) whether or not an attribute is
            // also selected, so a DM rolling AS a sheet with no attribute
            // still matches that sheet's rules.
            //
            // This reads state.LoadedDiceRules / state.HostHeldKeys outside the
            // Execute lock — intentional, and consistent with the RNG above
            // also running lock-free. Both are immutable snapshots mutated only
            // by rare host actions, so the worst case is evaluating against a
            // just-superseded rule set; the AppendRoll below is the only step
            // that needs the lock for state consistency.
            var appliedRules = ApplyLoadedDiceToRolls(
                state,
                caller,
                request,
                request.AttributeRef?.SheetId,
                rolls);

            if (request.Mode != RollMode.Normal)
            {
                int firstResult = rolls[0].Result;
                int secondResult = rolls[1].Result;
                bool firstIsKept = request.Mode == RollMode.Advantage
                    ? firstResult >= secondResult
                    : firstResult <= secondResult;

                rolls[firstIsKept ? 1 : 0] = rolls[firstIsKept ? 1 : 0] with { Discarded = true };
            }

            int total = rolls.Where(r => !r.Discarded).Sum(r => r.Result)
                + request.FlatModifier
                + (attributeModifier ?? 0);

            // Captured at roll time so the log can show the original request
            // shape rather than reconstructing it from the rolled dice.
            string formula = string.Join("+", request.Dice.Select(t => $"{t.Count}d{t.Sides}"));

            string? modifierBreakdown = null;
            if (contribution is AttributeContribution c && baseAttrValue is AttributeValue bv && attrName is not null)
            {
                // Two-stage explanation: first the value chain (base ± deltas),
                // then the dice expression. Lets the reader see *why* the
                // modifier ended up where it did when status effects re-shape
                // a Score-type attribute non-linearly (e.g. INT 14 − 5 → 9 → mod −1,
                // not 2 − 5 = −3).
                var valueParts = new List<string> { (bv.IntValue ?? 0).ToString() };
                foreach (var entry in c.ValueBreakdown.Skip(1))
                {
                    var sign = entry.Delta >= 0 ? "+" : "−";
                    valueParts.Add($"{sign} {Math.Abs(entry.Delta)} ({entry.Source})");
                }
                int effectiveRaw = c.EffectiveValue.IntValue ?? 0;
                string scoringTail = bv.Type == AttributeValueType.Score
                    ? $" = {effectiveRaw} → mod {FormatSigned(c.EffectiveModifier)}"
                    : $" = mod {FormatSigned(c.EffectiveModifier)}";

                int diceSum = rolls.Where(r => !r.Discarded).Sum(r => r.Result);
                var dicePieces = new List<string> { diceSum.ToString(), $"{(c.EffectiveModifier >= 0 ? "+" : "−")} {Math.Abs(c.EffectiveModifier)} ({attrName})" };
                if (request.FlatModifier != 0)
                    dicePieces.Add($"{(request.FlatModifier >= 0 ? "+" : "−")} {Math.Abs(request.FlatModifier)}");

                modifierBreakdown =
                    $"{attrName}: {string.Join(" ", valueParts)}{scoringTail}; "
                    + $"{string.Join(" ", dicePieces)} = {total}";
            }

            var result = new RollResult(
                Id: Guid.NewGuid(),
                RollerUserId: caller.Id,
                ForcedByUserId: null,
                Rolls: rolls,
                Total: total,
                Mode: request.Mode,
                FlatModifier: request.FlatModifier,
                AttributeModifier: attributeModifier,
                Label: request.Label ?? string.Empty,
                TimestampUtc: DateTime.UtcNow,
                Formula: formula,
                ModifierBreakdown: modifierBreakdown)
            {
                AppliedRules = appliedRules,
                OriginalDice = request.Dice.ToImmutableArray(),
                OriginalAttributeRef = request.AttributeRef,
            };

            var exec = state.Execute(() => state.AppendRoll(result));
            if (exec.IsCanceled) return ValueResult<RollResult>.FromCancellation();
            if (exec.TryGetFailure(out var err)) return ValueResult<RollResult>.FromError(err);
            return ValueResult<RollResult>.FromValue(result);
        }

        // ── Lifecycle verb ────────────────────────────────────────────────────────

        public Result EndSessionAsync(DndMapperGameState state, User caller)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may end the session.");
            if (state.IsDisposed) return Result.FromError("Session has already ended.");

            state.Dispose();
            return Result.Success;
        }

        /// <summary>
        /// Reverts the live session to a clean state without disposing it. Clears
        /// maps, sheets, rolls, settings, attribute schema and byte counters in
        /// one Execute. Saved templates (including built-ins) are preserved so
        /// the host doesn't lose their library when starting fresh.
        /// </summary>
        public Result ResetSessionAsync(DndMapperGameState state, User caller)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may reset the session.");
            if (state.IsDisposed) return Result.FromError("Session has already ended.");

            var exec = state.Execute(() =>
            {
                state.Maps = ImmutableArray<Map>.Empty;
                state.SetActiveMapId(null);
                state.Sheets = ImmutableDictionary<Guid, CharacterSheet>.Empty;
                state.RollLog = ImmutableList<RollResult>.Empty;
                state.SetSettings(new DndMapperSettings());
                state.SetAttributeSchema(AttributeSchema.FromPreset(AttributePreset.DnD5eCore));
                state.SetActiveSchemaTemplateId(DndMapperGameState.BuiltInDnD5eCoreId);
                state.SetInitiativeAttributeName(
                    state.CustomTemplates[DndMapperGameState.BuiltInDnD5eCoreId].InitiativeAttributeName);
                state.SetBytesUsed(0);
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var err)) return Result.FromError(err);
            return Result.Success;
        }

        // ── Image verbs ───────────────────────────────────────────────────────────

        /// <summary>
        /// Adds a host-uploaded image to a map. Pure-metadata: the bytes live in the
        /// host's IndexedDB and are reachable via <see cref="MapImage.ShareToken"/>.
        /// Validates content-type, per-file cap, and 1 GB room cap under the same lock
        /// that mutates state so two concurrent uploads can't both pass the cap check.
        /// </summary>
        public ValueResult<MapImage> AddImageAsync(DndMapperGameState state, User caller, Guid mapId, MapImage image)
        {
            if (state is null) return ValueResult<MapImage>.FromError("State is required.");
            if (caller is null) return ValueResult<MapImage>.FromError("Caller is required.");
            if (image is null) return ValueResult<MapImage>.FromError("Image is required.");
            if (!IsHost(state, caller)) return ValueResult<MapImage>.FromError("Only the host may add images.");
            if (image.ByteSize <= 0) return ValueResult<MapImage>.FromError("Image byte size must be positive.");
            if (image.ByteSize > PerFileCapBytes) return ValueResult<MapImage>.FromError("Image exceeds 100 MB per-file cap.");
            if (string.IsNullOrWhiteSpace(image.ContentType)
                || !AllowedImageContentTypes.Contains(image.ContentType))
                return ValueResult<MapImage>.FromError("Only PNG, JPEG, and WebP images are accepted.");

            string? error = null;
            MapImage? sealedImage = null;
            var exec = state.Execute(() =>
            {
                var mapIdx = state.IndexOfMap(mapId);
                if (mapIdx < 0) { error = "Unknown map id."; return; }
                var map = state.Maps[mapIdx];

                if (state.BytesUsed + image.ByteSize > PerRoomCapBytes)
                {
                    error = "Room exceeds 1 GB total image cap.";
                    return;
                }

                sealedImage = image with { LayerOrder = map.Images.Length };
                state.Maps = state.Maps.SetItem(mapIdx, map with
                {
                    Images = map.Images.Add(sealedImage),
                    ImagesVersion = map.ImagesVersion + 1,
                    ImagesMembershipVersion = map.ImagesMembershipVersion + 1,
                });
                state.AdjustBytesUsed(sealedImage.ByteSize);
            });

            if (exec.IsCanceled) return ValueResult<MapImage>.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return ValueResult<MapImage>.FromError(execErr);
            if (error is not null) return ValueResult<MapImage>.FromError(error);
            return ValueResult<MapImage>.FromValue(sealedImage!);
        }

        /// <summary>
        /// Updates the live blob-share token for an image, broadcast to all players via
        /// the StateChanged event. Host-only. Pass <c>null</c> to indicate the share is
        /// dead (player UIs render a placeholder until a new token arrives).
        /// </summary>
        public Result UpdateImageShareTokenAsync(DndMapperGameState state, User caller, Guid mapId, Guid imageId, Guid? newToken)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may update image share tokens.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (!TryUpdateImage(state, mapId, imageId, (img, _) => img.ShareToken == newToken ? img : img with { ShareToken = newToken }, bumpImagesVersion: true))
                {
                    error = "Unknown map or image id.";
                }
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        /// <summary>
        /// Atomically nulls every image's <see cref="MapImage.ShareToken"/>. Called on
        /// host detach so player UIs immediately render placeholders rather than
        /// receiving 410s from stale capability URLs. Single Execute, one notification.
        /// </summary>
        public Result ClearAllImageShareTokensAsync(DndMapperGameState state, User caller)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may clear image share tokens.");

            var exec = state.Execute(() =>
            {
                var newMaps = state.Maps;
                for (int mapIdx = 0; mapIdx < newMaps.Length; mapIdx++)
                {
                    var m = newMaps[mapIdx];
                    bool changed = false;
                    var newImages = m.Images;
                    for (int i = 0; i < newImages.Length; i++)
                    {
                        var image = newImages[i];
                        if (image.ShareToken is null) continue;
                        newImages = newImages.SetItem(i, image with { ShareToken = null });
                        changed = true;
                    }
                    if (changed)
                    {
                        newMaps = newMaps.SetItem(mapIdx, m with
                        {
                            Images = newImages,
                            ImagesVersion = m.ImagesVersion + 1,
                        });
                    }
                }
                state.Maps = newMaps;
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return Result.Success;
        }

        /// <summary>
        /// Reassigns a token's type and ownership. Host-only. Used when hydrating a
        /// previous session's player tokens (loaded as NPCToken with OwnerUserId=null)
        /// and giving them to a currently-registered player. Also accepts
        /// <see cref="TokenType.NPCToken"/> for arbitrary host-driven reassignment.
        /// </summary>
        public Result ReassignTokenOwnerAsync(DndMapperGameState state, User caller, Guid tokenId, string? newOwnerUserId, TokenType newType)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may reassign token ownership.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                var (token, map) = FindTokenAndMap(state, tokenId);
                if (token is null || map is null) { error = "Unknown token id."; return; }

                if (newType == TokenType.PlayerToken)
                {
                    if (string.IsNullOrWhiteSpace(newOwnerUserId))
                    {
                        error = "PlayerToken requires an owner user id.";
                        return;
                    }
                    if (!state.Players.Any(p => p.User.Id == newOwnerUserId))
                    {
                        error = "Target user is not a registered player.";
                        return;
                    }
                    if (map.Tokens.Any(t => t.Id != tokenId
                            && t.Type == TokenType.PlayerToken
                            && t.OwnerUserId == newOwnerUserId))
                    {
                        error = "Target player already owns a token on this map.";
                        return;
                    }

                    state.UpdateToken(map.Id, tokenId, t => t with
                    {
                        Type = TokenType.PlayerToken,
                        OwnerUserId = newOwnerUserId,
                        RepresentsUserId = null,
                    });
                }
                else if (newType == TokenType.NPCToken)
                {
                    if (newOwnerUserId is not null
                        && !state.Players.Any(p => p.User.Id == newOwnerUserId))
                    {
                        error = "Owner user id is not a registered player.";
                        return;
                    }
                    state.UpdateToken(map.Id, tokenId, t => t with
                    {
                        Type = TokenType.NPCToken,
                        OwnerUserId = newOwnerUserId, // null = host-owned NPC, non-null = player-owned NPC
                        RepresentsUserId = null,
                    });
                }
                else
                {
                    error = "Unsupported token type.";
                    return;
                }
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        /// <summary>
        /// Host-only. Promotes an <see cref="TokenType.NPCToken"/> to a
        /// <see cref="TokenType.PlayerToken"/> owned by
        /// <paramref name="newOwnerUserId"/>. If the token has an attached sheet,
        /// the sheet's ownership transfers too — and every other token across every
        /// map that references the same sheet is promoted in the same lock
        /// acquisition. This is the symmetric counterpart of the auto-conversion that
        /// runs when a player leaves mid-session (which orphans all of the player's
        /// tokens on every map, plus their sheet).
        /// </summary>
        /// <remarks>
        /// Rejects if the target player already owns a <see cref="TokenType.PlayerToken"/>
        /// on any affected map or already owns a different <see cref="CharacterSheet"/>;
        /// the host must resolve those conflicts first.
        /// </remarks>
        public Result AssignCharacterToPlayerAsync(DndMapperGameState state, User caller, Guid tokenId, string newOwnerUserId)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may assign characters to players.");
            if (string.IsNullOrWhiteSpace(newOwnerUserId)) return Result.FromError("Target player id is required.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                var (token, _) = FindTokenAndMap(state, tokenId);
                if (token is null) { error = "Unknown token id."; return; }

                CharacterSheet? sheet = null;
                if (token.SheetId is Guid sheetId)
                    state.Sheets.TryGetValue(sheetId, out sheet);

                error = AssignCharacterInternal(state, sheet, anchorToken: token, newOwnerUserId);
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        /// <summary>
        /// Host-only. Transfers ownership of a sheet (currently host/NPC-owned) to a
        /// registered player, and promotes every <see cref="TokenType.NPCToken"/>
        /// linked to that sheet across every map to a <see cref="TokenType.PlayerToken"/>
        /// owned by the same player. Use this for sheets that have no current token
        /// (e.g. a sheet built in advance, or an orphaned sheet whose tokens were
        /// already cleaned up).
        /// </summary>
        public Result AssignSheetToPlayerAsync(DndMapperGameState state, User caller, Guid sheetId, string newOwnerUserId)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may assign characters to players.");
            if (string.IsNullOrWhiteSpace(newOwnerUserId)) return Result.FromError("Target player id is required.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (!state.Sheets.TryGetValue(sheetId, out var sheet))
                {
                    error = "Unknown sheet id.";
                    return;
                }

                error = AssignCharacterInternal(state, sheet, anchorToken: null, newOwnerUserId);
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        // Shared promotion logic for AssignCharacterToPlayerAsync (token-keyed) and
        // AssignSheetToPlayerAsync (sheet-keyed). Must be called from inside Execute.
        // Returns null on success or an error message on failure (caller surfaces it
        // through Result.FromError).
        private static string? AssignCharacterInternal(
            DndMapperGameState state,
            CharacterSheet? sheet,
            Token? anchorToken,
            string newOwnerUserId)
        {
            if (!state.Players.Any(p => p.User.Id == newOwnerUserId))
                return "Target user is not a registered player.";

            // Two characters per player is out of scope. Block if the target already
            // owns *some other* sheet. (Transferring the same sheet to a different
            // player is allowed — that's the host re-assigning a character.)
            if (state.Sheets.Values.Any(s =>
                    s.OwnerUserId == newOwnerUserId && (sheet is null || s.Id != sheet.Id)))
                return "Target player already owns a character sheet.";

            // Collect every (mapId, tokenId) we'd promote: the anchor (if supplied)
            // plus every token across every map that references this sheet.
            var promoteIds = new HashSet<Guid>();
            if (anchorToken is not null) promoteIds.Add(anchorToken.Id);
            if (sheet is not null)
            {
                foreach (var map in state.Maps)
                    foreach (var t in map.Tokens)
                        if (t.SheetId == sheet.Id)
                            promoteIds.Add(t.Id);
            }

            var tokensToPromote = new List<Token>();
            foreach (var map in state.Maps)
                foreach (var t in map.Tokens)
                    if (promoteIds.Contains(t.Id))
                        tokensToPromote.Add(t);

            // PlayerTokens are allowed as sources — that's a transfer from one
            // player to another. Reject only the genuine no-op: every token is
            // already owned by the requested target.
            if (tokensToPromote.Count > 0
                && tokensToPromote.All(t => t.Type == TokenType.PlayerToken && t.OwnerUserId == newOwnerUserId))
            {
                return "Token is already owned by that player.";
            }

            // The target player must not already own a different PlayerToken on any
            // map we'd be promoting onto.
            foreach (var t in tokensToPromote)
            {
                var map = state.Maps.FirstOrDefault(m => m.Id == t.MapId);
                if (map is null) continue;
                if (map.Tokens.Any(other =>
                        other.Id != t.Id
                        && other.Type == TokenType.PlayerToken
                        && other.OwnerUserId == newOwnerUserId))
                    return "Target player already owns a token on one of the affected maps.";
            }

            // All validation passed — mutate.
            state.Maps = state.Maps
                .Select(m =>
                {
                    var changed = false;
                    var newTokens = m.Tokens;
                    for (int i = 0; i < newTokens.Length; i++)
                    {
                        var t = newTokens[i];
                        if (!promoteIds.Contains(t.Id)) continue;
                        newTokens = newTokens.SetItem(i, t with
                        {
                            Type = TokenType.PlayerToken,
                            OwnerUserId = newOwnerUserId,
                            RepresentsUserId = null,
                        });
                        changed = true;
                    }
                    return changed ? m with { Tokens = newTokens } : m;
                })
                .ToImmutableArray();

            if (sheet is not null)
            {
                state.UpdateSheet(sheet.Id, s => s with
                {
                    OwnerUserId = newOwnerUserId,
                    RepresentsUserId = null,
                });
            }

            return null;
        }

        public Result UpdateImageTransformAsync(
            DndMapperGameState state, User caller, Guid mapId, Guid imageId,
            double x, double y, double width, double height, double rotation, double opacity)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may update image transforms.");
            if (width <= 0 || height <= 0) return Result.FromError("Width and height must be positive.");
            if (opacity < 0.0 || opacity > 1.0) return Result.FromError("Opacity must be between 0.0 and 1.0.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (!TryUpdateImage(state, mapId, imageId, (img, _) =>
                {
                    if (img.Locked) return null;
                    return img with
                    {
                        X = x,
                        Y = y,
                        Width = width,
                        Height = height,
                        Rotation = rotation,
                        Opacity = opacity,
                    };
                }, bumpImagesVersion: true, out var rejection))
                {
                    error = rejection ?? "Unknown map or image id.";
                }
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public Result ReorderImageLayerAsync(DndMapperGameState state, User caller, Guid mapId, Guid imageId, int newLayerOrder)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may reorder image layers.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                var mapIdx = state.IndexOfMap(mapId);
                if (mapIdx < 0) { error = "Unknown map id."; return; }
                var map = state.Maps[mapIdx];

                int currentIndex = DndMapperGameState.IndexOfImage(map, imageId);
                if (currentIndex < 0) { error = "Unknown image id."; return; }
                if (map.Images[currentIndex].Locked) { error = "Image is locked."; return; }
                if (newLayerOrder < 0 || newLayerOrder >= map.Images.Length)
                {
                    error = "New layer order is out of range.";
                    return;
                }

                var image = map.Images[currentIndex];
                var reordered = map.Images.RemoveAt(currentIndex).Insert(newLayerOrder, image);
                var withLayerOrders = ImmutableArray.CreateBuilder<MapImage>();
                for (int i = 0; i < reordered.Length; i++)
                    withLayerOrders.Add(reordered[i] with { LayerOrder = i });

                state.Maps = state.Maps.SetItem(mapIdx, map with
                {
                    Images = withLayerOrders.ToImmutable(),
                    ImagesVersion = map.ImagesVersion + 1,
                });
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        /// <summary>
        /// Renames an image layer. Host-only. Trims the new name; empty strings
        /// are allowed (clears the name so the Layers panel falls back to its
        /// default "Layer #N" label).
        /// </summary>
        public Result SetImageNameAsync(DndMapperGameState state, User caller, Guid mapId, Guid imageId, string name)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may rename image layers.");

            var trimmed = (name ?? string.Empty).Trim();
            string? error = null;
            var exec = state.Execute(() =>
            {
                if (!TryUpdateImage(state, mapId, imageId, (img, _) => img.Name == trimmed ? img : img with { Name = trimmed }, bumpImagesVersion: true))
                {
                    error = "Unknown map or image id.";
                }
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        /// <summary>
        /// Locks or unlocks an image. Host-only. Locked images cannot have their
        /// transform changed or layer reordered until unlocked. Removal still works.
        /// </summary>
        public Result SetImageLockedAsync(DndMapperGameState state, User caller, Guid mapId, Guid imageId, bool locked)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may lock or unlock images.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                var mapIdx = state.IndexOfMap(mapId);
                if (mapIdx < 0) { error = "Unknown map or image id."; return; }
                var map = state.Maps[mapIdx];
                var imgIdx = DndMapperGameState.IndexOfImage(map, imageId);
                if (imgIdx < 0) { error = "Unknown map or image id."; return; }
                var image = map.Images[imgIdx];
                if (image.Locked == locked) return;

                state.Maps = state.Maps.SetItem(mapIdx, map with
                {
                    Images = map.Images.SetItem(imgIdx, image with { Locked = locked }),
                    ImagesVersion = map.ImagesVersion + 1,
                    ImagesMembershipVersion = map.ImagesMembershipVersion + 1,
                });
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        /// <summary>
        /// Hides or shows an image. Host-only. Hidden images are excluded from
        /// the canvas render for everyone and from the layer-selection grid.
        /// </summary>
        public Result SetImageHiddenAsync(DndMapperGameState state, User caller, Guid mapId, Guid imageId, bool hidden)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may hide or show images.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (!TryUpdateImage(state, mapId, imageId, (img, _) => img.Hidden == hidden ? img : img with { Hidden = hidden }, bumpImagesVersion: true))
                {
                    error = "Unknown map or image id.";
                }
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public Result RemoveImageAsync(DndMapperGameState state, User caller, Guid mapId, Guid imageId)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may remove images.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                var mapIdx = state.IndexOfMap(mapId);
                if (mapIdx < 0) { error = "Unknown map id."; return; }
                var map = state.Maps[mapIdx];
                int idx = DndMapperGameState.IndexOfImage(map, imageId);
                if (idx < 0) { error = "Unknown image id."; return; }

                var image = map.Images[idx];
                var withoutImage = map.Images.RemoveAt(idx);
                var withLayerOrders = ImmutableArray.CreateBuilder<MapImage>();
                for (int i = 0; i < withoutImage.Length; i++)
                    withLayerOrders.Add(withoutImage[i] with { LayerOrder = i });

                state.Maps = state.Maps.SetItem(mapIdx, map with
                {
                    Images = withLayerOrders.ToImmutable(),
                    ImagesVersion = map.ImagesVersion + 1,
                    ImagesMembershipVersion = map.ImagesMembershipVersion + 1,
                });
                state.AdjustBytesUsed(-image.ByteSize);
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        // Image-update common path: locates the image, applies the update lambda
        // (which may return null to signal "rejected — caller decides why"), and
        // optionally bumps ImagesVersion when the lambda returned a changed
        // image. Returns true when the lookup succeeded (even on a no-op or
        // rejection) so callers distinguish "image not found" from policy
        // rejections.
        private static bool TryUpdateImage(
            DndMapperGameState state,
            Guid mapId, Guid imageId,
            Func<MapImage, Map, MapImage?> update,
            bool bumpImagesVersion)
            => TryUpdateImage(state, mapId, imageId, update, bumpImagesVersion, out _);

        private static bool TryUpdateImage(
            DndMapperGameState state,
            Guid mapId, Guid imageId,
            Func<MapImage, Map, MapImage?> update,
            bool bumpImagesVersion,
            out string? rejection)
        {
            rejection = null;
            var mapIdx = state.IndexOfMap(mapId);
            if (mapIdx < 0) return false;
            var map = state.Maps[mapIdx];
            var imgIdx = DndMapperGameState.IndexOfImage(map, imageId);
            if (imgIdx < 0) return false;
            var current = map.Images[imgIdx];
            var next = update(current, map);
            if (next is null)
            {
                // Lookup succeeded but the update lambda rejected (e.g. image
                // is locked). Treated as a failure path so callers surface the
                // rejection rather than silently succeeding.
                rejection = "Image is locked.";
                return false;
            }
            if (ReferenceEquals(next, current)) return true;
            state.Maps = state.Maps.SetItem(mapIdx, map with
            {
                Images = map.Images.SetItem(imgIdx, next),
                ImagesVersion = bumpImagesVersion ? map.ImagesVersion + 1 : map.ImagesVersion,
            });
            return true;
        }

        // ── Fog of war ────────────────────────────────────────────────────────────

        /// <summary>
        /// Sets every cell in <paramref name="cells"/> to <paramref name="fogged"/>
        /// on the named map. Host-only. Out-of-bounds cells are silently dropped.
        /// An empty list is a no-op success so the client can flush a stroke that
        /// touched no new cells without seeing an error.
        ///
        /// Batched rebuild: the whole stroke produces one new ImmutableArray for
        /// FogMask and one FogVersion bump, instead of one allocation per cell.
        /// </summary>
        public Result PaintFogAsync(
            DndMapperGameState state,
            User caller,
            Guid mapId,
            IReadOnlyList<(int cx, int cy)> cells,
            bool fogged)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (cells is null) return Result.FromError("Cells list is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may change fog.");
            if (cells.Count == 0) return Result.Success;

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (!state.UpdateMap(mapId, m =>
                {
                    var totalBits = (long)m.Grid.WidthCells * m.Grid.HeightCells;
                    if (totalBits <= 0) return m;
                    var byteCount = (int)((totalBits + 7) / 8);
                    var workingMask = m.FogMask.IsDefaultOrEmpty
                        ? new byte[byteCount]
                        : m.FogMask.ToArray();
                    bool changed = false;
                    foreach (var (cx, cy) in cells)
                    {
                        if (cx < 0 || cy < 0 || cx >= m.Grid.WidthCells || cy >= m.Grid.HeightCells) continue;
                        var bit = cy * m.Grid.WidthCells + cx;
                        var idx = bit >> 3;
                        var bitMask = (byte)(1 << (bit & 7));
                        var before = workingMask[idx];
                        byte after = fogged ? (byte)(before | bitMask) : (byte)(before & ~bitMask);
                        if (after != before)
                        {
                            workingMask[idx] = after;
                            changed = true;
                        }
                    }
                    if (!changed) return m;
                    return m with
                    {
                        FogMask = ImmutableCollectionsMarshal.AsImmutableArray(workingMask),
                        FogVersion = m.FogVersion + 1,
                    };
                }))
                {
                    error = "Unknown map id.";
                }
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public Result RevealCellsAsync(DndMapperGameState state, User caller, Guid mapId, IReadOnlyList<(int cx, int cy)> cells)
            => PaintFogAsync(state, caller, mapId, cells, fogged: false);

        public Result HideCellsAsync(DndMapperGameState state, User caller, Guid mapId, IReadOnlyList<(int cx, int cy)> cells)
            => PaintFogAsync(state, caller, mapId, cells, fogged: true);

        public Result FillMapWithFogAsync(DndMapperGameState state, User caller, Guid mapId)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may change fog.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (!state.UpdateMap(mapId, m =>
                {
                    var totalBits = m.Grid.WidthCells * m.Grid.HeightCells;
                    if (totalBits <= 0)
                    {
                        return m.FogMask.IsDefaultOrEmpty
                            ? m
                            : m with { FogMask = ImmutableArray<byte>.Empty, FogVersion = m.FogVersion + 1 };
                    }

                    var bytes = (totalBits + 7) / 8;
                    var mask = new byte[bytes];
                    for (var i = 0; i < bytes; i++) mask[i] = 0xFF;

                    // Zero any trailing bits past WidthCells*HeightCells in the last
                    // byte so the serialized mask stays exact. IsFogged also bounds-
                    // checks, but keeping the storage clean makes save-roundtrip
                    // tests trivial to reason about.
                    var trailing = bytes * 8 - totalBits;
                    if (trailing > 0)
                    {
                        var keepLow = 8 - trailing;
                        mask[bytes - 1] = (byte)(mask[bytes - 1] & ((1 << keepLow) - 1));
                    }

                    return m with
                    {
                        FogMask = ImmutableCollectionsMarshal.AsImmutableArray(mask),
                        FogVersion = m.FogVersion + 1,
                    };
                }))
                {
                    error = "Unknown map id.";
                }
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public Result ClearAllFogAsync(DndMapperGameState state, User caller, Guid mapId)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may change fog.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (!state.UpdateMap(mapId, m =>
                {
                    if (m.FogMask.IsDefaultOrEmpty) return m;
                    return m with { FogMask = ImmutableArray<byte>.Empty, FogVersion = m.FogVersion + 1 };
                }))
                {
                    error = "Unknown map id.";
                }
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        // ── Internal helpers (must be called from inside Execute) ─────────────────

        private Guid SpawnPlayerTokenInternal(DndMapperGameState state, Guid mapId, User player)
        {
            // Reuse existing session-scoped sheet if the player already has one.
            var existingSheet = state.Sheets.Values.FirstOrDefault(s => s.OwnerUserId == player.Id);
            Guid sheetId;
            if (existingSheet is not null)
            {
                sheetId = existingSheet.Id;
            }
            else
            {
                sheetId = Guid.NewGuid();
                var sheet = new CharacterSheet
                {
                    Id = sheetId,
                    OwnerUserId = player.Id,
                    CharacterName = player.Name,
                    Color = DefaultColorPalette.FromName(player.Name),
                    Values = SeedSheetValues(state.AttributeSchema),
                };
                state.Sheets = state.Sheets.SetItem(sheetId, sheet);
            }

            var mapIdx = state.IndexOfMap(mapId);
            if (mapIdx < 0) return Guid.Empty;
            var map = state.Maps[mapIdx];
            var (sx, sy) = SpawnPosition(map);
            var tokenId = Guid.NewGuid();
            var token = new Token
            {
                Id = tokenId,
                Type = TokenType.PlayerToken,
                OwnerUserId = player.Id,
                RepresentsUserId = null,
                Name = player.Name,
                Color = DefaultColorPalette.FromName(player.Name),
                IconKind = TokenIconKind.Initial,
                MapId = mapId,
                X = sx,
                Y = sy,
                SheetId = sheetId,
            };
            state.Maps = state.Maps.SetItem(mapIdx, map with { Tokens = map.Tokens.Add(token) });
            return tokenId;
        }

        private static void ConvertAbandonedPlayerCharacterInternal(DndMapperGameState state, User departingPlayer)
        {
            state.Maps = state.Maps
                .Select(m =>
                {
                    var changed = false;
                    var newTokens = m.Tokens;
                    for (int i = 0; i < newTokens.Length; i++)
                    {
                        var token = newTokens[i];
                        if (token.Type == TokenType.PlayerToken && token.OwnerUserId == departingPlayer.Id)
                        {
                            newTokens = newTokens.SetItem(i, token with
                            {
                                Type = TokenType.NPCToken,
                                OwnerUserId = null,
                                RepresentsUserId = departingPlayer.Id,
                            });
                            changed = true;
                        }
                    }
                    return changed ? m with { Tokens = newTokens } : m;
                })
                .ToImmutableArray();

            // Orphan the player's sheet so the host can reassign it later.
            foreach (var (sheetId, sheet) in state.Sheets)
            {
                if (sheet.OwnerUserId == departingPlayer.Id)
                {
                    state.Sheets = state.Sheets.SetItem(sheetId, sheet with
                    {
                        OwnerUserId = null,
                        RepresentsUserId = departingPlayer.Id,
                    });
                }
            }
        }

        private static ImmutableDictionary<string, AttributeValue> SeedSheetValues(AttributeSchema schema)
        {
            var builder = ImmutableDictionary.CreateBuilder<string, AttributeValue>();
            foreach (var row in schema.Rows)
                builder[row.Name] = row.Default;
            return builder.ToImmutable();
        }

        // ── Permission / lookup helpers ───────────────────────────────────────────

        private static bool IsHost(DndMapperGameState state, User caller) => state.Host.Id == caller.Id;

        private static bool CanMoveToken(DndMapperGameState state, User caller, Token token)
        {
            return state.Settings.TokenMovement switch
            {
                TokenMovementPolicy.OwnerOrHost => IsHost(state, caller)
                    || (token.OwnerUserId is not null && token.OwnerUserId == caller.Id),
                TokenMovementPolicy.Anyone => IsHost(state, caller)
                    || state.Players.Any(p => p.User.Id == caller.Id),
                TokenMovementPolicy.HostOnly => IsHost(state, caller),
                _ => false,
            };
        }

        private static bool CanEditSheet(DndMapperGameState state, User caller, CharacterSheet sheet)
        {
            if (IsHost(state, caller)) return true;
            return state.Settings.SheetEditByOthers switch
            {
                SheetEditPolicy.HostOnly => false,
                SheetEditPolicy.OwnersAndHost => sheet.OwnerUserId is not null && sheet.OwnerUserId == caller.Id,
                SheetEditPolicy.Anyone => state.Players.Any(p => p.User.Id == caller.Id),
                _ => false,
            };
        }

        private static (Token? Token, Map? Map) FindTokenAndMap(DndMapperGameState state, Guid tokenId)
        {
            foreach (var map in state.Maps)
            {
                var token = map.Tokens.FirstOrDefault(t => t.Id == tokenId);
                if (token is not null) return (token, map);
            }
            return (null, null);
        }

        private static (double X, double Y) MapCenter(Map map) =>
            // Floor-then-center keeps spawns on a cell center regardless of even/odd
            // grid dimensions (W/2.0 lands on a corner for even widths).
            (Math.Floor(map.Grid.WidthCells / 2.0) + 0.5,
             Math.Floor(map.Grid.HeightCells / 2.0) + 0.5);

        private static (double X, double Y) SpawnPosition(Map map)
        {
            var (rawX, rawY) = map.DefaultSpawnPosition ?? MapCenter(map);
            // Snap any caller-supplied or default position to the nearest in-bounds
            // cell center so freshly spawned tokens never sit on a grid intersection.
            return SnapToGridHelper.Snap(rawX, rawY, map.Grid);
        }

        // If the caller (e.g. the host's UI) supplied an explicit spawn anchor —
        // typically the center of the current viewport — snap+clamp it onto a
        // cell. Otherwise fall back to the map's default spawn position.
        private static (double X, double Y) ResolveSpawn(Map map, double? atX, double? atY) =>
            atX is double x && atY is double y
                ? SnapToGridHelper.Snap(x, y, map.Grid)
                : SpawnPosition(map);
    }
}
