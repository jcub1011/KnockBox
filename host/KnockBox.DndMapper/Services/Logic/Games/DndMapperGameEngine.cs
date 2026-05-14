using KnockBox.Core.Plugins;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.Games.Http;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace KnockBox.DndMapper.Services.Logic.Games
{
    // M01 verb names use the GDD's `Async` suffix for cross-reference with the design doc,
    // but the bodies are synchronous (Execute is sync). Return types are plain Result / ValueResult<T>.
    public sealed class DndMapperGameEngine : AbstractGameEngine, IGameEngineHttpHandler
    {
        private const int MaxRollDiceCount = 20;
        private const int MaxNameLength = 60;
        private static readonly int[] AllowedDieSides = [4, 6, 8, 10, 12, 20, 100];

        // M03 image caps — see GDD §5 and the M03 milestone doc.
        private const long PerFileCapBytes = 5L * 1024 * 1024;
        private const long PerRoomCapBytes = 10L * 1024 * 1024;
        // Large enough to (a) MIME-sniff the magic bytes and (b) locate JPEG SOF markers
        // past EXIF/JFIF metadata for intrinsic dimension extraction.
        private const int SniffHeadLength = 4096;

        private readonly ILogger<DndMapperGameEngine> _logger;
        private readonly ILogger<DndMapperGameState> _stateLogger;
        private readonly IRandomNumberService _rng;
        private readonly IPluginStorage _storage;

        public DndMapperGameEngine(
            IPluginContext context,
            ILogger<DndMapperGameEngine> logger,
            ILogger<DndMapperGameState> stateLogger,
            IRandomNumberService rng)
            : base(maxPlayerCount: 8, minPlayerCount: 1)
        {
            _logger = logger;
            _stateLogger = stateLogger;
            _rng = rng;
            // Throws PluginCapabilityNotGrantedException if "Storage" is missing from plugin.json.
            _storage = context.Storage;
        }

        public override Task<ValueResult<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default)
        {
            if (host is null)
                return Task.FromResult(ValueResult<AbstractGameState>.FromError(
                    "Failed to create game state.",
                    $"Parameter {nameof(host)} was null."));

            var state = new DndMapperGameState(host, _stateLogger);
            state.Execute(() => state.SetJoinable(true));
            state.PlayerUnregistered += player => HandlePlayerLeft(state, player);
            // Closure captures only the Guid (not the state) so there is no cross-session leak.
            // AbstractGameState.Dispose nulls OnStateDisposed after firing.
            var sessionId = state.SessionId;
            state.OnStateDisposed += () => CleanupRoomStorage(sessionId);
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
                    int slot = 0;
                    foreach (var entry in state.Players)
                    {
                        SpawnPlayerTokenInternal(state, activeMap, entry.User, slot++);
                    }
                }
            });

            return Task.FromResult(executeResult);
        }

        // ── Player lifecycle ──────────────────────────────────────────────────────

        private void HandlePlayerLeft(DndMapperGameState state, User player)
        {
            // PlayerUnregistered fires OUTSIDE the lock — re-entering Execute is safe.
            state.Execute(() => ConvertAbandonedPlayerTokensInternal(state, player));
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
            // Snapshot images BEFORE removing the map so we can delete files OUTSIDE the
            // execute lock. Reduces lock-hold time and lets storage I/O fail without
            // rolling back the in-memory mutation.
            List<string> filesToDelete = [];
            var exec = state.Execute(() =>
            {
                var map = state.Maps.FirstOrDefault(m => m.Id == mapId);
                if (map is null) { error = "Unknown map id."; return; }

                long deltaBytes = 0;
                foreach (var image in map.Images)
                {
                    filesToDelete.Add(image.RelativePath);
                    deltaBytes += image.ByteSize;
                }
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
            if (error is not null) return Result.FromError(error);

            foreach (var path in filesToDelete)
            {
                try { _storage.Delete(path); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete image file [{Path}] during map delete; will retry at session end.", path);
                }
            }

            return Result.Success;
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

                int slot = 0;
                foreach (var entry in state.Players)
                {
                    if (!map.Tokens.Any(t => t.Type == TokenType.PlayerToken && t.OwnerUserId == entry.User.Id))
                        SpawnPlayerTokenInternal(state, map, entry.User, slot);
                    slot++;
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
            if (newGrid.WidthCells < 5 || newGrid.WidthCells > 200)
                return Result.FromError("Grid width must be between 5 and 200 cells.");
            if (newGrid.HeightCells < 5 || newGrid.HeightCells > 200)
                return Result.FromError("Grid height must be between 5 and 200 cells.");
            if (newGrid.CellPixels < 1)
                return Result.FromError("Cell pixel size must be at least 1.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                var map = state.Maps.FirstOrDefault(m => m.Id == mapId);
                if (map is null) { error = "Unknown map id."; return; }
                map.Grid = newGrid.Clone();
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

                int slot = -1;
                User? targetUser = null;
                for (int i = 0; i < state.Players.Count; i++)
                {
                    if (state.Players[i].User.Id == userId)
                    {
                        slot = i;
                        targetUser = state.Players[i].User;
                        break;
                    }
                }
                if (targetUser is null)
                {
                    error = "Target user is not a registered player.";
                    return;
                }

                newTokenId = SpawnPlayerTokenInternal(state, map, targetUser, slot);
            });

            if (exec.IsCanceled) return ValueResult<Guid>.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return ValueResult<Guid>.FromError(execErr);
            if (error is not null) return ValueResult<Guid>.FromError(error);
            return ValueResult<Guid>.FromValue(newTokenId);
        }

        public ValueResult<Guid> SpawnNpcTokenAsync(DndMapperGameState state, User caller, Guid mapId, string name)
        {
            if (state is null) return ValueResult<Guid>.FromError("State is required.");
            if (caller is null) return ValueResult<Guid>.FromError("Caller is required.");
            if (string.IsNullOrWhiteSpace(name)) return ValueResult<Guid>.FromError("Token name is required.");

            bool isHost = IsHost(state, caller);
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

                newId = Guid.NewGuid();
                var (cx, cy) = MapCenter(map);
                map.Tokens.Add(new Token
                {
                    Id = newId,
                    Type = TokenType.NPCToken,
                    OwnerUserId = isHost ? null : caller.Id,
                    Name = name,
                    Color = DefaultColorPalette.Neutral,
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

        public ValueResult<Guid> SpawnHostExtraTokenAsync(DndMapperGameState state, User caller, Guid mapId, string name, string? representsUserId)
        {
            if (state is null) return ValueResult<Guid>.FromError("State is required.");
            if (caller is null) return ValueResult<Guid>.FromError("Caller is required.");
            if (string.IsNullOrWhiteSpace(name)) return ValueResult<Guid>.FromError("Token name is required.");
            if (!IsHost(state, caller)) return ValueResult<Guid>.FromError("Only the host may spawn extra tokens.");

            Guid newId = default;
            string? error = null;
            var exec = state.Execute(() =>
            {
                var map = state.Maps.FirstOrDefault(m => m.Id == mapId);
                if (map is null) { error = "Unknown map id."; return; }

                string color = DefaultColorPalette.Neutral;
                if (representsUserId is not null)
                {
                    int slot = state.Players.ToList().FindIndex(p => p.User.Id == representsUserId);
                    if (slot >= 0) color = DefaultColorPalette.ForPlayerSlot(slot);
                }

                newId = Guid.NewGuid();
                var (cx, cy) = MapCenter(map);
                map.Tokens.Add(new Token
                {
                    Id = newId,
                    Type = TokenType.HostExtraToken,
                    OwnerUserId = null,
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

                token.X = newX;
                token.Y = newY;
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
        /// Pass <c>null</c> to detach. The sheet must exist if non-null.
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
                if (sheetId is Guid sid && !state.Sheets.ContainsKey(sid))
                {
                    error = "Unknown sheet id.";
                    return;
                }
                token.SheetId = sheetId;
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            return error is null ? Result.Success : Result.FromError(error);
        }

        /// <summary>
        /// Sets or clears the player a host-extra token represents. Host-only.
        /// Only valid on <see cref="TokenType.HostExtraToken"/>. Pass <c>null</c> to clear.
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
                if (token.Type != TokenType.HostExtraToken)
                {
                    error = "Only host-extra tokens may represent a player.";
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
            if (!IsHost(state, caller)) return ValueResult<Guid>.FromError("Only the host may create sheets directly.");

            Guid newId = Guid.NewGuid();
            var exec = state.Execute(() =>
            {
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

        public Result ChangeSchemaAsync(DndMapperGameState state, User caller, AttributeSchema newSchema)
        {
            if (state is null) return Result.FromError("State is required.");
            if (caller is null) return Result.FromError("Caller is required.");
            if (newSchema is null) return Result.FromError("Schema is required.");
            if (!IsHost(state, caller)) return Result.FromError("Only the host may change the attribute schema.");

            var exec = state.Execute(() =>
            {
                state.SetAttributeSchema(newSchema);

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
                if (request.Dice.Count != 1 || request.Dice[0].Count != 1 || request.Dice[0].Sides != 20)
                    return ValueResult<RollResult>.FromError("Advantage/Disadvantage requires exactly one d20.");
            }

            int? attributeModifier = null;
            if (request.AttributeRef is AttributeRef attrRef)
            {
                if (!state.Sheets.TryGetValue(attrRef.SheetId, out var sheet))
                    return ValueResult<RollResult>.FromError("Unknown sheet id for attribute reference.");
                if (!sheet.Values.TryGetValue(attrRef.AttributeName, out var attrValue))
                    return ValueResult<RollResult>.FromError("Unknown attribute name on the referenced sheet.");
                if (sheet.OwnerUserId is not null && sheet.OwnerUserId != caller.Id && !IsHost(state, caller))
                    return ValueResult<RollResult>.FromError("You may only reference your own attributes (or be host).");

                var modifier = attrValue.GetModifier();
                if (modifier is null)
                    return ValueResult<RollResult>.FromError("Referenced attribute does not produce a numeric modifier.");
                attributeModifier = modifier;
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
                int second = _rng.GetRandomInt(1, 21, RandomType.Fast);
                rolls.Add(new DieRoll(20, second, false));

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
                TimestampUtc: DateTime.UtcNow);

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

        // ── Image verbs (M03) ─────────────────────────────────────────────────────

        public ValueResult<MapImage> AddImageAsync(DndMapperGameState state, User caller, Guid mapId, MapImage image)
        {
            if (state is null) return ValueResult<MapImage>.FromError("State is required.");
            if (caller is null) return ValueResult<MapImage>.FromError("Caller is required.");
            if (image is null) return ValueResult<MapImage>.FromError("Image is required.");
            if (!IsHost(state, caller)) return ValueResult<MapImage>.FromError("Only the host may add images.");

            string? error = null;
            var exec = state.Execute(() =>
            {
                var map = state.Maps.FirstOrDefault(m => m.Id == mapId);
                if (map is null) { error = "Unknown map id."; return; }

                image.LayerOrder = map.Images.Count;
                map.Images.Add(image);
                state.AdjustBytesUsed(image.ByteSize);
            });

            if (exec.IsCanceled) return ValueResult<MapImage>.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return ValueResult<MapImage>.FromError(execErr);
            if (error is not null) return ValueResult<MapImage>.FromError(error);
            return ValueResult<MapImage>.FromValue(image);
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
                var (image, _) = FindImageAndMap(state, mapId, imageId);
                if (image is null) { error = "Unknown map or image id."; return; }
                if (image.Locked) { error = "Image is locked."; return; }

                image.X = x;
                image.Y = y;
                image.Width = width;
                image.Height = height;
                image.Rotation = rotation;
                image.Opacity = opacity;
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
                var (image, _) = FindImageAndMap(state, mapId, imageId);
                if (image is null) { error = "Unknown map or image id."; return; }
                image.Locked = locked;
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
            string? pathToDelete = null;
            var exec = state.Execute(() =>
            {
                var map = state.Maps.FirstOrDefault(m => m.Id == mapId);
                if (map is null) { error = "Unknown map id."; return; }

                int idx = map.Images.FindIndex(i => i.Id == imageId);
                if (idx < 0) { error = "Unknown image id."; return; }

                var image = map.Images[idx];
                pathToDelete = image.RelativePath;
                map.Images.RemoveAt(idx);
                state.AdjustBytesUsed(-image.ByteSize);

                for (int i = 0; i < map.Images.Count; i++)
                    map.Images[i].LayerOrder = i;
            });

            if (exec.IsCanceled) return Result.FromCancellation();
            if (exec.TryGetFailure(out var execErr)) return Result.FromError(execErr);
            if (error is not null) return Result.FromError(error);

            if (pathToDelete is not null)
            {
                try { _storage.Delete(pathToDelete); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete image file [{Path}] on remove; orphan will be cleaned up at session end.", pathToDelete);
                }
            }

            return Result.Success;
        }

        /// <summary>
        /// Host-only in-process upload verb. Called directly from the host's Blazor
        /// circuit (no HTTP boundary, no antiforgery, no cookie). Caller identity is
        /// trustworthy because it comes from the circuit-bound <see cref="User"/>.
        /// </summary>
        public async ValueTask<ValueResult<MapImage>> SaveImageAsync(
            DndMapperGameState state,
            User caller,
            Guid mapId,
            Stream fileStream,
            long declaredLength,
            CancellationToken ct = default)
        {
            if (state is null) return ValueResult<MapImage>.FromError("State is required.");
            if (caller is null) return ValueResult<MapImage>.FromError("Caller is required.");
            if (fileStream is null) return ValueResult<MapImage>.FromError("File stream is required.");
            if (!IsHost(state, caller)) return ValueResult<MapImage>.FromError("Only the host may upload images.");

            if (declaredLength <= 0)
                return ValueResult<MapImage>.FromError("Declared length must be positive.");
            if (declaredLength > PerFileCapBytes)
                return ValueResult<MapImage>.FromError("Image exceeds 5 MB per-file cap.");

            // Pre-flight: map exists + room cap. Also capture CellPixels so we can convert
            // intrinsic pixel dimensions to cell units after the upload completes.
            string? prefError = null;
            int cellPixels = 1;
            state.WithExclusiveRead(() =>
            {
                var map = state.Maps.FirstOrDefault(m => m.Id == mapId);
                if (map is null)
                {
                    prefError = "Unknown map id.";
                    return;
                }
                if (state.BytesUsed + declaredLength > PerRoomCapBytes)
                {
                    prefError = "Room exceeds 10 MB total image cap.";
                    return;
                }
                cellPixels = Math.Max(1, map.Grid.CellPixels);
            });
            if (prefError is not null) return ValueResult<MapImage>.FromError(prefError);

            // Sniff first bytes for MIME detection.
            var head = new byte[SniffHeadLength];
            int read = await fileStream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
            var sniffedMime = MimeSniffer.Detect(head.AsSpan(0, read));
            if (sniffedMime is null)
                return ValueResult<MapImage>.FromError("Only PNG, JPEG, and WebP images are accepted.");

            string ext = MimeSniffer.ExtensionFor(sniffedMime);
            string fileId = Guid.NewGuid().ToString();
            string relativePath = $"{state.SessionId}/images/{fileId}.{ext}";

            long writtenBytes;
            try
            {
                using (var output = _storage.OpenWrite(relativePath))
                {
                    await output.WriteAsync(head.AsMemory(0, read), ct);
                    writtenBytes = read;

                    var copyBuffer = new byte[81920];
                    int n;
                    while ((n = await fileStream.ReadAsync(copyBuffer, ct)) > 0)
                    {
                        // Defense-in-depth: refuse to write past the per-file cap even if declaredLength lied.
                        if (writtenBytes + n > PerFileCapBytes)
                        {
                            output.Dispose();
                            TryDelete(relativePath);
                            return ValueResult<MapImage>.FromError("Image stream exceeded 5 MB per-file cap.");
                        }
                        await output.WriteAsync(copyBuffer.AsMemory(0, n), ct);
                        writtenBytes += n;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                TryDelete(relativePath);
                return ValueResult<MapImage>.FromCancellation();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write uploaded image to storage at [{Path}].", relativePath);
                TryDelete(relativePath);
                return ValueResult<MapImage>.FromError("Storage write failed.");
            }

            double defaultW = 10;
            double defaultH = 10;
            double originalW = 0;
            double originalH = 0;
            if (ImageDimensionSniffer.TryDetect(head.AsSpan(0, read), sniffedMime, out int pxW, out int pxH))
            {
                defaultW = pxW / (double)cellPixels;
                defaultH = pxH / (double)cellPixels;
                originalW = defaultW;
                originalH = defaultH;
            }

            var image = new MapImage
            {
                Id = Guid.NewGuid(),
                RelativePath = relativePath,
                X = 0,
                Y = 0,
                Width = defaultW,
                Height = defaultH,
                OriginalWidth = originalW,
                OriginalHeight = originalH,
                Rotation = 0,
                Opacity = 1.0,
                LayerOrder = 0, // overwritten by AddImageAsync to map.Images.Count
                Locked = false,
                ByteSize = writtenBytes,
            };

            var addResult = AddImageAsync(state, caller, mapId, image);
            if (!addResult.TryGetSuccess(out var added))
            {
                TryDelete(relativePath);
                return addResult;
            }
            return ValueResult<MapImage>.FromValue(added);
        }

        // ── HTTP handler (M03) ────────────────────────────────────────────────────

        public ValueTask<IResult> HandleAsync(
            HttpContext context,
            string roomUri,
            AbstractGameState abstractState,
            string subPath,
            CancellationToken ct)
        {
            if (abstractState is not DndMapperGameState state)
                return ValueTask.FromResult<IResult>(Results.NotFound());

            // Only GET images/{id} is exposed via HTTP. Upload is in-process (see SaveImageAsync).
            if (HttpMethods.IsGet(context.Request.Method) && subPath.StartsWith("images/", StringComparison.Ordinal))
            {
                var idPart = subPath["images/".Length..];
                return ValueTask.FromResult(HandleImageServe(idPart, state, context));
            }

            return ValueTask.FromResult<IResult>(Results.NotFound());
        }

        private IResult HandleImageServe(string idStr, DndMapperGameState state, HttpContext context)
        {
            if (!Guid.TryParse(idStr, out var imageId))
                return Results.NotFound();

            // No further auth check — knowing the room URI is the access control,
            // matching how Blazor circuits load images via _content/...
            MapImage? image = null;
            state.WithExclusiveRead(() =>
            {
                foreach (var map in state.Maps)
                {
                    var found = map.Images.FirstOrDefault(i => i.Id == imageId);
                    if (found is not null) { image = found; break; }
                }
            });

            if (image is null) return Results.NotFound();

            Stream stream;
            try { stream = _storage.OpenRead(image.RelativePath); }
            catch (FileNotFoundException) { return Results.NotFound(); }
            catch (DirectoryNotFoundException) { return Results.NotFound(); }

            string contentType = MimeSniffer.ContentTypeForExtension(image.RelativePath) ?? "application/octet-stream";

            // `private` keeps room-scoped images out of shared caches even if the room URI leaks
            // through a CDN/proxy. Header set BEFORE returning the IResult so the dispatcher's
            // ExecuteAsync writes status/body without clearing the header dictionary.
            context.Response.Headers["Cache-Control"] = "private, max-age=3600";

            return Results.Stream(
                stream,
                contentType: contentType,
                enableRangeProcessing: true,
                lastModified: null,
                entityTag: new EntityTagHeaderValue($"\"{image.Id:N}\""));
        }

        // ── Storage cleanup ───────────────────────────────────────────────────────

        private void CleanupRoomStorage(Guid sessionId)
        {
            string prefix = $"{sessionId}/images";
            try
            {
                foreach (var rel in _storage.EnumerateFiles(prefix, "*"))
                {
                    try { _storage.Delete(rel); }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete [{Path}] during session cleanup.", rel);
                    }
                }
            }
            catch (DirectoryNotFoundException) { /* nothing to clean up */ }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Storage cleanup failed for session [{SessionId}].", sessionId);
            }
        }

        private void TryDelete(string relativePath)
        {
            try { _storage.Delete(relativePath); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Best-effort delete of [{Path}] failed.", relativePath);
            }
        }

        // ── Internal helpers (must be called from inside Execute) ─────────────────

        private Guid SpawnPlayerTokenInternal(DndMapperGameState state, Map map, User player, int slot)
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
                Color = DefaultColorPalette.ForPlayerSlot(slot),
                IconKind = TokenIconKind.Initial,
                MapId = map.Id,
                X = sx,
                Y = sy,
                SheetId = sheetId,
            });
            return tokenId;
        }

        private static void ConvertAbandonedPlayerTokensInternal(DndMapperGameState state, User departingPlayer)
        {
            foreach (var map in state.Maps)
            {
                foreach (var token in map.Tokens)
                {
                    if (token.Type == TokenType.PlayerToken && token.OwnerUserId == departingPlayer.Id)
                    {
                        token.Type = TokenType.HostExtraToken;
                        token.OwnerUserId = null;
                        token.RepresentsUserId = departingPlayer.Id;
                    }
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
                SheetEditPolicy.OwnersOnly => sheet.OwnerUserId is not null && sheet.OwnerUserId == caller.Id,
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
            (map.Grid.WidthCells / 2.0, map.Grid.HeightCells / 2.0);

        private static (double X, double Y) SpawnPosition(Map map) =>
            map.DefaultSpawnPosition ?? MapCenter(map);
    }
}
