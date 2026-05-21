using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Helpers;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;

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
        private const long PerRoomCapBytes = 1024L * 1024 * 1024;

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

                if (state.ActiveMapId is null && state.Maps.Count > 0)
                    state.SetActiveMapId(state.Maps.OrderBy(m => m.ListOrder).First().Id);

                if (state.ActiveMapId is Guid mapId &&
                    state.Maps.FirstOrDefault(m => m.Id == mapId) is Map activeMap)
                {
                    foreach (var entry in state.Players)
                    {
                        SpawnPlayerTokenInternal(state, activeMap, entry.User);
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
                state.Maps.Add(new Map
                {
                    Id = newId,
                    Name = name,
                    ListOrder = state.Maps.Count,
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
                var map = state.Maps.FirstOrDefault(m => m.Id == mapId);
                if (map is null) { error = "Unknown map id."; return; }
                map.Name = newName;
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
                var map = state.Maps.FirstOrDefault(m => m.Id == mapId);
                if (map is null) { error = "Unknown map id."; return; }

                long deltaBytes = 0;
                foreach (var image in map.Images)
                    deltaBytes += image.ByteSize;
                if (deltaBytes > 0) state.AdjustBytesUsed(-deltaBytes);

                state.Maps.Remove(map);

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
                    Grid = source.Grid.Clone(),
                    ListOrder = state.Maps.Count,
                    CreatedUtc = DateTime.UtcNow,
                    DefaultSpawnPosition = source.DefaultSpawnPosition,
                };
                state.Maps.Add(clone);
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

                for (int i = 0; i < orderedIds.Count; i++)
                {
                    var map = state.Maps.First(m => m.Id == orderedIds[i]);
                    map.ListOrder = i;
                }
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
                    if (!map.Tokens.Any(t => t.Type == TokenType.PlayerToken && t.OwnerUserId == entry.User.Id))
                        SpawnPlayerTokenInternal(state, map, entry.User);
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
                var map = state.Maps.FirstOrDefault(m => m.Id == mapId);
                if (map is null) { error = "Unknown map id."; return; }
                bool gridDimsChanged = map.Grid.WidthCells != newGrid.WidthCells
                    || map.Grid.HeightCells != newGrid.HeightCells;
                map.Grid = newGrid.Clone();
                // Mask bit layout is keyed off WidthCells; treat any dim change as a
                // fog mutation so canvas memoization rebuilds the polygon.
                if (gridDimsChanged && map.FogMask.Length > 0) map.FogVersion++;

                // Tokens are stored at cell-center coordinates; clamp any that fall
                // outside the new bounds to the nearest in-bounds cell center.
                // Images are intentionally left as-is so the host can reposition them.
                double maxX = Math.Max(0.5, map.Grid.WidthCells - 0.5);
                double maxY = Math.Max(0.5, map.Grid.HeightCells - 0.5);
                foreach (var token in map.Tokens)
                {
                    token.X = Math.Clamp(token.X, 0.5, maxX);
                    token.Y = Math.Clamp(token.Y, 0.5, maxY);
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
                    state.Maps.FirstOrDefault(m => m.Id == mapId) is not Map map)
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

                newTokenId = SpawnPlayerTokenInternal(state, map, targetUser);
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
                var map = state.Maps.FirstOrDefault(m => m.Id == mapId);
                if (map is null) { error = "Unknown map id."; return; }

                string color = DefaultColorPalette.FromName(name);

                newId = Guid.NewGuid();
                var (cx, cy) = ResolveSpawn(map, atX, atY);
                map.Tokens.Add(new Token
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
                });
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
                token.X = sx;
                token.Y = sy;
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
                var (token, _) = FindTokenAndMap(state, tokenId);
                if (token is null) { error = "Unknown token id."; return; }

                bool isHost = IsHost(state, caller);
                bool isOwner = token.OwnerUserId is not null && token.OwnerUserId == caller.Id;
                if (!isHost && !isOwner) { error = "You are not permitted to update this token."; return; }

                token.Name = name;
                token.Color = color;
                token.IconKind = iconKind;

                // Propagate to a linked sheet so the two stay in sync. Guarded
                // by value-compare to avoid pointless StateChanged churn.
                if (token.SheetId is Guid linkedSheetId
                    && state.Sheets.TryGetValue(linkedSheetId, out var linkedSheet)
                    && linkedSheet.CharacterName != name)
                {
                    linkedSheet.CharacterName = name;
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

                map.Tokens.Remove(token);
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
                var (token, _) = FindTokenAndMap(state, tokenId);
                if (token is null) { error = "Unknown token id."; return; }
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
                    if (!string.IsNullOrWhiteSpace(sheet.CharacterName)
                        && token.Name != sheet.CharacterName)
                    {
                        token.Name = sheet.CharacterName;
                    }
                    else if (!string.IsNullOrWhiteSpace(token.Name)
                        && sheet.CharacterName != token.Name)
                    {
                        sheet.CharacterName = token.Name;
                    }

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
                        sheet.OwnerUserId = ownerId;
                        sheet.RepresentsUserId = null;
                    }
                }
                token.SheetId = sheetId;
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
                var (token, _) = FindTokenAndMap(state, tokenId);
                if (token is null) { error = "Unknown token id."; return; }
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
                token.RepresentsUserId = representsUserId;
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
                var (token, _) = FindTokenAndMap(state, tokenId);
                if (token is null) { error = "Unknown token id."; return; }
                token.Hidden = hidden;
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
                };
                SeedSheetValues(sheet, state.AttributeSchema);
                state.Sheets[newId] = sheet;
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

                sheet.Values[attributeName] = value;
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

                sheet.CharacterName = characterName;
                sheet.Notes = notes ?? string.Empty;
                sheet.Hp = hp;
                sheet.MaxHp = maxHp;

                // Propagate rename to every linked token so the token roster
                // and SVG label stay in sync with the sheet.
                foreach (var map in state.Maps)
                {
                    foreach (var token in map.Tokens)
                    {
                        if (token.SheetId == sheetId && token.Name != characterName)
                            token.Name = characterName;
                    }
                }
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        public Result DeleteSheetAsync(DndMapperGameState state, User caller, Guid sheetId)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may delete sheets.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (!state.Sheets.Remove(sheetId)) { error = "Unknown sheet id."; return; }

                foreach (var map in state.Maps)
                {
                    foreach (var token in map.Tokens)
                    {
                        if (token.SheetId == sheetId)
                            token.SheetId = null;
                    }
                }
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

                foreach (var sheet in state.Sheets.Values)
                {
                    var oldValues = new Dictionary<string, AttributeValue>(sheet.Values);
                    sheet.Values.Clear();
                    foreach (var row in newSchema.Rows)
                    {
                        if (oldValues.TryGetValue(row.Name, out var existing) && existing.Type == row.Type)
                            sheet.Values[row.Name] = existing;
                        else
                            sheet.Values[row.Name] = row.Default;
                    }
                }
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var err)) return Result.FromError(err);
            return Result.Success;
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

                state.CustomTemplates[newId] = new NamedTemplate
                {
                    Id = newId,
                    Name = trimmed,
                    Rows = [.. state.AttributeSchema.Rows],
                };
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

                state.CustomTemplates[newId] = new NamedTemplate
                {
                    Id = newId,
                    Name = trimmed,
                    Rows = [.. rows],
                };
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
                state.CustomTemplates.Remove(templateId);

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
                    foreach (var sheet in state.Sheets.Values)
                    {
                        var oldValues = new Dictionary<string, AttributeValue>(sheet.Values);
                        sheet.Values.Clear();
                        foreach (var row in fallback.Rows)
                        {
                            if (oldValues.TryGetValue(row.Name, out var keep) && keep.Type == row.Type)
                                sheet.Values[row.Name] = keep;
                            else
                                sheet.Values[row.Name] = row.Default;
                        }
                    }
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
                existing.Rows = [.. rows];
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
                existing.Name = trimmed;
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

            var exec = state.Execute(() => state.SetSettings(newSettings.Clone()));

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var err)) return Result.FromError(err);
            return Result.Success;
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

                var combat = new CombatState { Phase = CombatPhase.WaitingForRolls, RoundNumber = 1 };
                var seenTokenIds = new HashSet<Guid>();

                if (npcTokenIds is not null)
                {
                    foreach (var tokenId in npcTokenIds)
                    {
                        var (token, _) = FindTokenAndMap(state, tokenId);
                        if (token is null) { error = $"Unknown token id {tokenId}."; return; }
                        if (token.Type != TokenType.NPCToken) { error = "Only NPC tokens can be added to combat selection."; return; }
                        if (!seenTokenIds.Add(tokenId)) continue;
                        combat.TurnOrder.Add(new CombatantEntry
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
                    combat.TurnOrder.Add(new CombatantEntry
                    {
                        Id = Guid.NewGuid(),
                        TokenId = token?.Id ?? Guid.Empty,
                        Name = token?.Name ?? entry.User.Name,
                        OwnerUserId = entry.User.Id,
                    });
                }

                if (combat.TurnOrder.Count == 0) { error = "Cannot start initiative with no combatants."; return; }

                state.SetActiveCombat(combat);
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
            int total = d20 + modifier;

            string? error = null;
            var exec = state.Execute(() =>
            {
                if (state.ActiveCombat is null) { error = "No active combat."; return; }
                if (state.ActiveCombat.Phase != CombatPhase.WaitingForRolls) { error = "Initiative phase has ended."; return; }
                var entry = state.ActiveCombat.TurnOrder.FirstOrDefault(e => e.OwnerUserId == caller.Id);
                if (entry is null) { error = "You are not a combatant in this initiative."; return; }
                if (entry.InitiativeRoll is not null) return; // no-op

                entry.InitiativeRoll = total;
                state.AppendRoll(BuildInitiativeRollResult(caller.Id, null, d20, modifier, total));
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
                var entry = state.ActiveCombat.TurnOrder.FirstOrDefault(e => e.Id == combatantId);
                if (entry is null) { error = "Unknown combatant id."; return; }
                if (entry.OwnerUserId is not null) { error = "Use ForceInitiativeRollAsync for player combatants."; return; }

                entry.InitiativeRoll = roll;
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
                var entry = state.ActiveCombat.TurnOrder.FirstOrDefault(e => e.Id == combatantId);
                if (entry is null) { error = "Unknown combatant id."; return; }
                if (entry.OwnerUserId is null) { error = "Force-roll is only valid for player combatants."; return; }
                if (entry.InitiativeRoll is not null) { error = "Player has already rolled."; return; }

                int modifier = ResolveInitiativeModifier(state, entry.OwnerUserId);
                int total = d20 + modifier;
                entry.InitiativeRoll = total;
                entry.IsForceRolled = true;
                state.AppendRoll(BuildInitiativeRollResult(entry.OwnerUserId, caller.Id, d20, modifier, total));
                TryTransitionToActive(state);
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        // Rolls initiative for every NPC combatant that hasn't yet rolled, in
        // a single Execute. Each NPC gets its own d20 + modifier (resolved
        // from its attached character sheet, if any — NPCs without a sheet
        // roll bare d20). Already-rolled NPCs are skipped so a host who
        // entered a number manually keeps it. Each roll is logged as
        // ForcedByUserId = caller so the audit trail attributes the bulk roll
        // to the host.
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

                foreach (var entry in state.ActiveCombat.TurnOrder)
                {
                    if (entry.OwnerUserId is not null) continue;       // players
                    if (entry.InitiativeRoll is not null) continue;    // already rolled or manually set

                    var sheet = FindSheetForToken(state, entry.TokenId);
                    int modifier = ResolveInitiativeModifierForSheet(state, sheet);
                    int d20 = _rng.GetRandomInt(1, 21, RandomType.Fast);
                    int total = d20 + modifier;

                    entry.InitiativeRoll = total;
                    // The loop above skips player entries, so NPCs are the
                    // only thing here; the host is recorded as both roller
                    // and forcing party so the audit log attributes the
                    // bulk roll to the host that triggered it.
                    state.AppendRoll(BuildInitiativeRollResult(
                        rollerUserId: caller.Id,
                        forcedByUserId: caller.Id,
                        d20: d20,
                        dexModifier: modifier,
                        total: total));
                }

                TryTransitionToActive(state);
            });
            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
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
                if (state.ActiveCombat.TurnOrder.Count == 0) { error = "Turn order is empty."; return; }

                state.ActiveCombat.CurrentTurnIndex++;
                if (state.ActiveCombat.CurrentTurnIndex >= state.ActiveCombat.TurnOrder.Count)
                {
                    state.ActiveCombat.CurrentTurnIndex = 0;
                    state.ActiveCombat.RoundNumber++;
                }
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
                if (state.ActiveCombat.TurnOrder.Any(e => e.TokenId == tokenId))
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
                int insertIdx = TurnOrderSorter.FindInsertionIndex(state.ActiveCombat.TurnOrder, entry);
                state.ActiveCombat.TurnOrder.Insert(insertIdx, entry);

                if (state.ActiveCombat.Phase == CombatPhase.Active
                    && insertIdx <= state.ActiveCombat.CurrentTurnIndex)
                {
                    state.ActiveCombat.CurrentTurnIndex++;
                }
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
                var idx = state.ActiveCombat.TurnOrder.FindIndex(e => e.Id == combatantId);
                if (idx < 0) { error = "Unknown combatant id."; return; }

                bool removingCurrent = state.ActiveCombat.Phase == CombatPhase.Active && idx == state.ActiveCombat.CurrentTurnIndex;
                state.ActiveCombat.TurnOrder.RemoveAt(idx);

                if (state.ActiveCombat.TurnOrder.Count == 0)
                {
                    state.SetActiveCombat(null);
                    return;
                }

                if (state.ActiveCombat.Phase == CombatPhase.Active)
                {
                    if (idx < state.ActiveCombat.CurrentTurnIndex)
                    {
                        state.ActiveCombat.CurrentTurnIndex--;
                    }
                    else if (removingCurrent)
                    {
                        // Stay at idx (which now points to the next combatant), wrap if past end.
                        if (state.ActiveCombat.CurrentTurnIndex >= state.ActiveCombat.TurnOrder.Count)
                        {
                            state.ActiveCombat.CurrentTurnIndex = 0;
                            state.ActiveCombat.RoundNumber++;
                        }
                    }
                }
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
            state.ActiveCombat.TurnOrder.Clear();
            state.ActiveCombat.TurnOrder.AddRange(sorted);
            state.ActiveCombat.Phase = CombatPhase.Active;
            state.ActiveCombat.CurrentTurnIndex = 0;
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
                var template = state.GetActiveSchemaTemplate();
                if (template is not null)
                    template.InitiativeAttributeName = string.IsNullOrEmpty(attributeName) ? null : attributeName;
            });
            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        private static string FormatSigned(int value)
            => value >= 0 ? $"+{value}" : $"−{Math.Abs(value)}";

        private static RollResult BuildInitiativeRollResult(string rollerUserId, string? forcedByUserId, int d20, int dexModifier, int total)
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
                Label: "Initiative",
                TimestampUtc: DateTime.UtcNow,
                Formula: "1d20",
                ModifierBreakdown: null);
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
                var map = state.Maps.FirstOrDefault(m => m.Id == mapId);
                if (map is null) { error = "Unknown map id."; return; }
                map.MarkupSvg = sanitized;
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
                    AttributeDeltas = attributeDeltas is null ? [] : [.. attributeDeltas],
                    MaxHpDelta = maxHpDelta,
                    OnApplyHpDelta = onApplyHpDelta,
                    Notes = notes ?? string.Empty,
                    AppliedUtc = DateTime.UtcNow,
                };
                sheet.StatusEffects.Add(effect);

                if (sheet.Hp is int hp && onApplyHpDelta is int delta)
                    sheet.Hp = hp + delta;

                ClampHpToEffectiveMax(sheet);
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
                var effect = sheet.StatusEffects.FirstOrDefault(e => e.Id == effectId);
                if (effect is null) { error = "Unknown status effect id."; return; }

                effect.Name = name;
                effect.AttributeDeltas = attributeDeltas is null ? [] : [.. attributeDeltas];
                effect.MaxHpDelta = maxHpDelta;
                effect.OnApplyHpDelta = onApplyHpDelta;
                effect.Notes = notes ?? string.Empty;

                ClampHpToEffectiveMax(sheet);
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
                var idx = sheet.StatusEffects.FindIndex(e => e.Id == effectId);
                if (idx < 0) { error = "Unknown status effect id."; return; }
                sheet.StatusEffects.RemoveAt(idx);
                ClampHpToEffectiveMax(sheet);
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
                schema.StatusEffectTemplates.Add(new StatusEffectTemplate
                {
                    Id = newId,
                    Name = name,
                    AttributeDeltas = attributeDeltas is null ? [] : [.. attributeDeltas],
                    MaxHpDelta = maxHpDelta,
                    OnApplyHpDelta = onApplyHpDelta,
                    Notes = notes ?? string.Empty,
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
                var template = schema.StatusEffectTemplates.FirstOrDefault(t => t.Id == templateId);
                if (template is null) { error = "Unknown template id."; return; }

                template.Name = name;
                template.AttributeDeltas = attributeDeltas is null ? [] : [.. attributeDeltas];
                template.MaxHpDelta = maxHpDelta;
                template.OnApplyHpDelta = onApplyHpDelta;
                template.Notes = notes ?? string.Empty;
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
                var idx = schema.StatusEffectTemplates.FindIndex(t => t.Id == templateId);
                if (idx < 0) { error = "Unknown template id."; return; }
                schema.StatusEffectTemplates.RemoveAt(idx);
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        private static void ClampHpToEffectiveMax(CharacterSheet sheet)
        {
            var effectiveMax = EffectiveMaxHpResolver.ResolveEffectiveMaxHp(sheet);
            if (effectiveMax is int max && sheet.Hp is int hp && hp > max)
                sheet.Hp = max;
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

            var exec = state.Execute(() => state.GlobalRollTemplates.Add(template));
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
                int idx = state.GlobalRollTemplates.FindIndex(t => t.Id == templateId);
                if (idx < 0) { error = "Unknown template id."; return; }
                state.GlobalRollTemplates[idx] = new RollTemplate(
                    templateId, name, [.. dice], flatModifier, mode,
                    string.IsNullOrWhiteSpace(attributeName) ? null : attributeName,
                    label ?? string.Empty, RollTemplateScope.Global);
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
                int idx = state.GlobalRollTemplates.FindIndex(t => t.Id == templateId);
                if (idx < 0) { error = "Unknown template id."; return; }
                state.GlobalRollTemplates.RemoveAt(idx);
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

            var exec = state.Execute(() => sheet.RollTemplates.Add(template));
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
                int idx = sheet.RollTemplates.FindIndex(t => t.Id == templateId);
                if (idx < 0) { error = "Unknown template id."; return; }
                sheet.RollTemplates[idx] = new RollTemplate(
                    templateId, name, [.. dice], flatModifier, mode,
                    string.IsNullOrWhiteSpace(attributeName) ? null : attributeName,
                    label ?? string.Empty, RollTemplateScope.Sheet);
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
                int idx = sheet.RollTemplates.FindIndex(t => t.Id == templateId);
                if (idx < 0) { error = "Unknown template id."; return; }
                sheet.RollTemplates.RemoveAt(idx);
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
            if (request.AttributeRef is AttributeRef attrRef)
            {
                if (!state.Sheets.TryGetValue(attrRef.SheetId, out var sheet))
                    return ValueResult<RollResult>.FromError("Unknown sheet id for attribute reference.");
                if (!sheet.Values.TryGetValue(attrRef.AttributeName, out var attrValue))
                    return ValueResult<RollResult>.FromError("Unknown attribute name on the referenced sheet.");
                if (sheet.OwnerUserId is not null && sheet.OwnerUserId != caller.Id && !IsHost(state, caller))
                    return ValueResult<RollResult>.FromError("You may only reference your own attributes (or be host).");

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
                ModifierBreakdown: modifierBreakdown);

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
                state.Maps.Clear();
                state.SetActiveMapId(null);
                state.Sheets.Clear();
                state.RollLog.Clear();
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
            var exec = state.Execute(() =>
            {
                var map = state.Maps.FirstOrDefault(m => m.Id == mapId);
                if (map is null) { error = "Unknown map id."; return; }

                if (state.BytesUsed + image.ByteSize > PerRoomCapBytes)
                {
                    error = "Room exceeds 1 GB total image cap.";
                    return;
                }

                image.LayerOrder = map.Images.Count;
                map.Images.Add(image);
                map.ImagesVersion++;
                map.ImagesMembershipVersion++;
                state.AdjustBytesUsed(image.ByteSize);
            });

            if (exec.IsCanceled) return ValueResult<MapImage>.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return ValueResult<MapImage>.FromError(execErr);
            if (error is not null) return ValueResult<MapImage>.FromError(error);
            return ValueResult<MapImage>.FromValue(image);
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
                var (image, imageMap) = FindImageAndMap(state, mapId, imageId);
                if (image is null) { error = "Unknown map or image id."; return; }
                if (image.ShareToken == newToken) return;
                image.ShareToken = newToken;
                if (imageMap is not null) imageMap.ImagesVersion++;
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
                foreach (var map in state.Maps)
                {
                    bool changed = false;
                    foreach (var image in map.Images)
                    {
                        if (image.ShareToken is null) continue;
                        image.ShareToken = null;
                        changed = true;
                    }
                    if (changed) map.ImagesVersion++;
                }
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

                    token.Type = TokenType.PlayerToken;
                    token.OwnerUserId = newOwnerUserId;
                    token.RepresentsUserId = null;
                }
                else if (newType == TokenType.NPCToken)
                {
                    if (newOwnerUserId is not null
                        && !state.Players.Any(p => p.User.Id == newOwnerUserId))
                    {
                        error = "Owner user id is not a registered player.";
                        return;
                    }
                    token.Type = TokenType.NPCToken;
                    token.OwnerUserId = newOwnerUserId; // null = host-owned NPC, non-null = player-owned NPC
                    token.RepresentsUserId = null;
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

            // Collect every token that should be promoted: the anchor (if supplied)
            // plus every token across every map that references this sheet.
            var tokensToPromote = new List<Token>();
            if (sheet is not null)
            {
                foreach (var map in state.Maps)
                    foreach (var t in map.Tokens)
                        if (t.SheetId == sheet.Id)
                            tokensToPromote.Add(t);
            }
            if (anchorToken is not null && !tokensToPromote.Contains(anchorToken))
                tokensToPromote.Add(anchorToken);

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
            foreach (var t in tokensToPromote)
            {
                t.Type = TokenType.PlayerToken;
                t.OwnerUserId = newOwnerUserId;
                t.RepresentsUserId = null;
            }
            if (sheet is not null)
            {
                sheet.OwnerUserId = newOwnerUserId;
                sheet.RepresentsUserId = null;
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
                var (image, imageMap) = FindImageAndMap(state, mapId, imageId);
                if (image is null) { error = "Unknown map or image id."; return; }
                if (image.Locked) { error = "Image is locked."; return; }

                image.X = x;
                image.Y = y;
                image.Width = width;
                image.Height = height;
                image.Rotation = rotation;
                image.Opacity = opacity;
                if (imageMap is not null) imageMap.ImagesVersion++;
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
                var map = state.Maps.FirstOrDefault(m => m.Id == mapId);
                if (map is null) { error = "Unknown map id."; return; }

                int currentIndex = map.Images.FindIndex(i => i.Id == imageId);
                if (currentIndex < 0) { error = "Unknown image id."; return; }
                if (map.Images[currentIndex].Locked) { error = "Image is locked."; return; }
                if (newLayerOrder < 0 || newLayerOrder >= map.Images.Count)
                {
                    error = "New layer order is out of range.";
                    return;
                }

                var image = map.Images[currentIndex];
                map.Images.RemoveAt(currentIndex);
                map.Images.Insert(newLayerOrder, image);

                for (int i = 0; i < map.Images.Count; i++)
                    map.Images[i].LayerOrder = i;
                map.ImagesVersion++;
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
                var (image, imageMap) = FindImageAndMap(state, mapId, imageId);
                if (image is null) { error = "Unknown map or image id."; return; }
                if (image.Name == trimmed) return;
                image.Name = trimmed;
                if (imageMap is not null) imageMap.ImagesVersion++;
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
                var (image, imageMap) = FindImageAndMap(state, mapId, imageId);
                if (image is null) { error = "Unknown map or image id."; return; }
                if (image.Locked == locked) return;
                image.Locked = locked;
                if (imageMap is not null)
                {
                    imageMap.ImagesVersion++;
                    imageMap.ImagesMembershipVersion++;
                }
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
                var (image, imageMap) = FindImageAndMap(state, mapId, imageId);
                if (image is null) { error = "Unknown map or image id."; return; }
                if (image.Hidden == hidden) return;
                image.Hidden = hidden;
                if (imageMap is not null) imageMap.ImagesVersion++;
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
                var map = state.Maps.FirstOrDefault(m => m.Id == mapId);
                if (map is null) { error = "Unknown map id."; return; }

                int idx = map.Images.FindIndex(i => i.Id == imageId);
                if (idx < 0) { error = "Unknown image id."; return; }

                var image = map.Images[idx];
                image.ShareToken = null;
                map.Images.RemoveAt(idx);
                state.AdjustBytesUsed(-image.ByteSize);

                for (int i = 0; i < map.Images.Count; i++)
                    map.Images[i].LayerOrder = i;
                map.ImagesVersion++;
                map.ImagesMembershipVersion++;
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        // ── Fog of war ────────────────────────────────────────────────────────────

        /// <summary>
        /// Sets every cell in <paramref name="cells"/> to <paramref name="fogged"/>
        /// on the named map. Host-only. Out-of-bounds cells are silently dropped
        /// by <see cref="Map.SetFogged"/>. An empty list is a no-op success so the
        /// client can flush a stroke that touched no new cells without seeing an error.
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
                var map = state.Maps.FirstOrDefault(m => m.Id == mapId);
                if (map is null) { error = "Unknown map id."; return; }
                foreach (var (cx, cy) in cells)
                    map.SetFogged(cx, cy, fogged);
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
                var map = state.Maps.FirstOrDefault(m => m.Id == mapId);
                if (map is null) { error = "Unknown map id."; return; }

                var totalBits = map.Grid.WidthCells * map.Grid.HeightCells;
                if (totalBits <= 0)
                {
                    if (map.FogMask.Length > 0) { map.FogMask = []; map.FogVersion++; }
                    return;
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

                map.FogMask = mask;
                map.FogVersion++;
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
                var map = state.Maps.FirstOrDefault(m => m.Id == mapId);
                if (map is null) { error = "Unknown map id."; return; }
                if (map.FogMask.Length == 0) return;
                map.FogMask = [];
                map.FogVersion++;
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        // ── Internal helpers (must be called from inside Execute) ─────────────────

        private Guid SpawnPlayerTokenInternal(DndMapperGameState state, Map map, User player)
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
                };
                SeedSheetValues(sheet, state.AttributeSchema);
                state.Sheets[sheetId] = sheet;
            }

            var (sx, sy) = SpawnPosition(map);
            var tokenId = Guid.NewGuid();
            map.Tokens.Add(new Token
            {
                Id = tokenId,
                Type = TokenType.PlayerToken,
                OwnerUserId = player.Id,
                RepresentsUserId = null,
                Name = player.Name,
                Color = DefaultColorPalette.FromName(player.Name),
                IconKind = TokenIconKind.Initial,
                MapId = map.Id,
                X = sx,
                Y = sy,
                SheetId = sheetId,
            });
            return tokenId;
        }

        private static void ConvertAbandonedPlayerCharacterInternal(DndMapperGameState state, User departingPlayer)
        {
            foreach (var map in state.Maps)
            {
                foreach (var token in map.Tokens)
                {
                    if (token.Type == TokenType.PlayerToken && token.OwnerUserId == departingPlayer.Id)
                    {
                        token.Type = TokenType.NPCToken;
                        token.OwnerUserId = null;
                        token.RepresentsUserId = departingPlayer.Id;
                    }
                }
            }

            foreach (var sheet in state.Sheets.Values)
            {
                if (sheet.OwnerUserId == departingPlayer.Id)
                {
                    sheet.OwnerUserId = null;
                    sheet.RepresentsUserId = departingPlayer.Id;
                }
            }
        }

        private static void SeedSheetValues(CharacterSheet sheet, AttributeSchema schema)
        {
            sheet.Values.Clear();
            foreach (var row in schema.Rows)
                sheet.Values[row.Name] = row.Default;
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

        private static (MapImage? Image, Map? Map) FindImageAndMap(DndMapperGameState state, Guid mapId, Guid imageId)
        {
            var map = state.Maps.FirstOrDefault(m => m.Id == mapId);
            if (map is null) return (null, null);
            var image = map.Images.FirstOrDefault(i => i.Id == imageId);
            return (image, image is null ? null : map);
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
