using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.Games.Engines.Shared;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.DiceSimulator;
using KnockBox.Services.State.Games.DiceSimulator.Data;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;

namespace KnockBox.Services.Navigation.Games.DiceSimulator
{
    public class DiceSimulatorGameEngine(
        IRandomNumberService randomNumberService,
        ILogger<DiceSimulatorGameEngine> logger,
        ILogger<DiceSimulatorGameState> stateLogger) : AbstractGameEngine
    {
        public override async Task<Result<AbstractGameState>> CreateStateAsync(User host, CancellationToken ct = default)
        {
            if (host is null)
                return Result.FromError<AbstractGameState>(new ArgumentNullException(nameof(host)));

            var gameState = new DiceSimulatorGameState(host, stateLogger);
            gameState.UpdateJoinableStatus(true);
            logger.LogInformation("Created gameState with user [{userId}] as host.", host.Id);
            return Result.FromValue<AbstractGameState>(gameState);
        }

        public override async Task<Result> StartAsync(User host, AbstractGameState state, CancellationToken ct = default)
        {
            if (state is not DiceSimulatorGameState gameState)
                return Result.FromError(new InvalidCastException($"Game state of type [{(state?.GetType().Name ?? "null")}] couldn't be cast to type [{nameof(DiceSimulatorGameState)}]."));

            if (host != gameState.Host)
                return Result.FromError(new InvalidOperationException($"Only the host can start the game."));

            var executeResult = state.Execute(() =>
            {
                state.UpdateJoinableStatus(false);
            });

            if (executeResult.IsFailure) return executeResult;
            return Result.Success;
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
            if (user != state.Host) return Result.FromError(new InvalidOperationException("Only the host can clear history."));
            return state.Execute(() =>
            {
                state.ClearHistory();
            });
        }
    }
}
