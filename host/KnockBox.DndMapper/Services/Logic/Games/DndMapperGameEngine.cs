using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
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
            state.PlayerUnregistered += player => HandlePlayerLeft(state, player);
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
            var exec = state.Execute(() =>
            {
                var map = state.Maps.FirstOrDefault(m => m.Id == mapId);
                if (map is null) { error = "Unknown map id."; return; }

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
