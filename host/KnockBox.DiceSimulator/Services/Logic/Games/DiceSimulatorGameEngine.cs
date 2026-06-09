using System.Text.Json;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Games.Shared.Projection;
using KnockBox.DiceSimulator.Contracts;
using KnockBox.DiceSimulator.Services.Projection;
using KnockBox.DiceSimulator.Services.State.Games;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;

namespace KnockBox.DiceSimulator.Services.Logic.Games
{
    public class DiceSimulatorGameEngine(
        IRandomNumberService randomNumberService,
        ILogger<DiceSimulatorGameEngine> logger,
        ILogger<DiceSimulatorGameState> stateLogger)
        : AbstractGameEngine<DiceSimulatorGameState>, IGameStateProjector, IGameCommandHandler
    {
        private readonly DiceSimulatorStateProjector _projector = new();

        // Match the hub's wire format: enums as strings, case-insensitive property
        // names, so a client-serialized DiceRollAction payload deserializes here.
        private static readonly JsonSerializerOptions CommandJsonOptions = new()
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            PropertyNameCaseInsensitive = true,
        };

        /// <summary>Per-recipient projection entry point used by the host's <c>GameViewCoordinator</c>.</summary>
        public object? ProjectFor(AbstractGameState state, Guid recipientId)
            => ((IGameStateProjector)_projector).ProjectFor(state, recipientId);

        /// <summary>
        /// Maps a hub command name to the same engine method a Razor page used to call
        /// directly. Host-identity authorization lives in the invoked methods.
        /// </summary>
        public async ValueTask<Result> HandleCommandAsync(
            User caller, AbstractGameState state, string command, string? payloadJson, CancellationToken ct = default)
        {
            if (state is not DiceSimulatorGameState s)
                return Result.FromError("Invalid game state for Dice Simulator.");

            return command switch
            {
                DiceSimulatorCommands.Start        => await StartAsync(caller, s, ct),
                DiceSimulatorCommands.RollDice     => RollDiceFromPayload(caller, s, payloadJson),
                DiceSimulatorCommands.ClearHistory => ClearHistory(caller, s),
                DiceSimulatorCommands.KickPlayer   => KickFromPayload(caller, s, payloadJson),
                _ => Result.FromError($"Unknown command [{command}].")
            };
        }

        private Result RollDiceFromPayload(User caller, DiceSimulatorGameState state, string? payloadJson)
        {
            if (string.IsNullOrEmpty(payloadJson))
                return Result.FromError("Missing roll payload.");

            DiceRollAction? action;
            try { action = JsonSerializer.Deserialize<DiceRollAction>(payloadJson, CommandJsonOptions); }
            catch (JsonException) { return Result.FromError("Malformed roll payload."); }

            if (action is null)
                return Result.FromError("Malformed roll payload.");

            return RollDice(caller, state, action);
        }

        private Result KickFromPayload(User caller, DiceSimulatorGameState state, string? payloadJson)
        {
            if (string.IsNullOrEmpty(payloadJson))
                return Result.FromError("Missing kick payload.");

            Guid targetId;
            try { targetId = JsonSerializer.Deserialize<Guid>(payloadJson, CommandJsonOptions); }
            catch (JsonException) { return Result.FromError("Malformed kick payload."); }

            var target = state.Players.FirstOrDefault(e => e.User.Id == targetId).User;
            if (target is null)
                return Result.FromError("Player is not in this game.");

            return state.KickPlayer(caller, target);
        }

        public override async Task<ValueResult<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default)
        {
            if (host is null)
                return ValueResult<AbstractGameState>.FromError("Failed to create game state.", $"Parameter {nameof(host)} was null.");

            var gameState = new DiceSimulatorGameState(host, stateLogger);
            gameState.Execute(() => gameState.SetJoinable(true));
            logger.LogInformation("Created gameState with user [{userId}] as host.", host.Id);
            return gameState;
        }

        protected override Task<Result> StartAsyncCore(DiceSimulatorGameState state, CancellationToken ct = default)
        {
            var executeResult = state.Execute(() =>
            {
                state.SetJoinable(false);
            });

            if (executeResult.IsFailure) return Task.FromResult(executeResult);
            return Task.FromResult(Result.Success);
        }

        public Result RollDice(User player, DiceSimulatorGameState state, DiceRollAction action)
        {
            return state.Execute(() =>
            {
                int diceCount = Math.Max(1, Math.Min(99, action.DiceCount));
                int[] rawRolls = new int[diceCount];
                int[]? altRolls = null;
                
                int sides = (int)action.DiceType;
                
                for (int i = 0; i < diceCount; i++)
                {
                    rawRolls[i] = randomNumberService.GetRandomInt(1, sides + 1, RandomType.Fast);
                }
                
                if (action.Mode == RollMode.Advantage || action.Mode == RollMode.Disadvantage)
                {
                    altRolls = new int[diceCount];
                    for (int i = 0; i < diceCount; i++)
                    {
                        altRolls[i] = randomNumberService.GetRandomInt(1, sides + 1, RandomType.Fast);
                    }
                }
                
                int rawTotal = rawRolls.Sum();
                int altTotal = altRolls?.Sum() ?? 0;
                
                int keptTotal = rawTotal;
                int discardedTotal = altTotal;
                
                if (action.Mode == RollMode.Advantage)
                {
                    if (altTotal > rawTotal)
                    {
                        keptTotal = altTotal;
                        discardedTotal = rawTotal;
                        
                        var temp = rawRolls;
                        rawRolls = altRolls!;
                        altRolls = temp;
                    }
                }
                else if (action.Mode == RollMode.Disadvantage)
                {
                    if (altTotal < rawTotal)
                    {
                        keptTotal = altTotal;
                        discardedTotal = rawTotal;
                        
                        var temp = rawRolls;
                        rawRolls = altRolls!;
                        altRolls = temp;
                    }
                }
                
                int result = keptTotal + action.Modifier;
                
                string modifierStr = action.Modifier == 0 ? "" : (action.Modifier > 0 ? $"+{action.Modifier}" : action.Modifier.ToString());
                string expression = $"{diceCount}d{sides}{modifierStr}";
                
                var entry = new DiceRollEntry
                {
                    Id = Guid.NewGuid(),
                    PlayerId = player.Id,
                    PlayerName = player.Name,
                    DiceType = action.DiceType,
                    DiceCount = diceCount,
                    Modifier = action.Modifier,
                    Mode = action.Mode,
                    Result = result,
                    RawRolls = rawRolls,
                    AltRolls = altRolls,
                    AltTotal = discardedTotal,
                    Expression = expression,
                    Timestamp = DateTimeOffset.UtcNow
                };
                
                state.AddRoll(entry);
                
                var stats = state.GetOrAddPlayerStats(player.Id, player.Name);
                
                lock (stats)
                {
                    stats.TotalRolls++;
                    stats.TotalDiceRolled += diceCount;
                    
                    stats.RollCountByDie.TryAdd(action.DiceType, 0);
                    stats.RollCountByDie[action.DiceType] += diceCount;
                    
                    if (diceCount == 1 && action.DiceType == DiceType.D20)
                    {
                        int keptDie = rawRolls[0];
                        if (keptDie == 20) stats.NatTwentyCount++;
                        if (keptDie == 1) stats.NatOneCount++;
                    }
                    
                    if (result > stats.HighestResult || stats.TotalRolls == 1)
                    {
                        stats.HighestResult = result;
                        stats.HighestResultExpression = expression;
                    }
                    
                    stats.CumulativeTotal += result;
                }
            });
        }
        
        public Result ClearHistory(User user, DiceSimulatorGameState state)
        {
            // Compare by Id, not reference: in the hub model each command resolves a
            // fresh User instance from the connection token, so reference equality
            // (the old `user != state.Host`) would reject even the real host.
            if (user.Id != state.Host.Id) return Result.FromError("Only the host can clear history.");
            return state.Execute(() =>
            {
                state.ClearHistory();
            });
        }
    }
}
