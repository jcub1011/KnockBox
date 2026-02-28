using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.DiceSimulator.Data;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;
using System.Collections.Concurrent;
using KnockBox.Extensions.Returns;

namespace KnockBox.Services.State.Games.DiceSimulator
{
    public class DiceSimulatorGameState(
        User host,
        ILogger<DiceSimulatorGameState> logger, 
        IRandomNumberService randomNumberService)
        : AbstractGameState(host, logger)
    {
        private readonly List<DiceRollEntry> _rollHistory = new();
        private readonly ConcurrentDictionary<string, PlayerStats> _playerStats = new();

        public IReadOnlyList<DiceRollEntry> RollHistory 
        { 
            get 
            {
                lock (_rollHistory)
                {
                    return _rollHistory.ToList();
                }
            } 
        }
        
        public IReadOnlyDictionary<string, PlayerStats> PlayerStats => _playerStats;

        public Result RollDice(User player, DiceRollAction action)
        {
            return Execute(() =>
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
                
                lock (_rollHistory)
                {
                    _rollHistory.Add(entry);
                }
                
                var stats = _playerStats.GetOrAdd(player.Id, _ => new PlayerStats { PlayerName = player.Name });
                
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
        
        public Result ClearHistory(User user)
        {
            if (user != Host) return Result.FromError(new InvalidOperationException("Only the host can clear history."));
            return Execute(() =>
            {
                lock (_rollHistory)
                {
                    _rollHistory.Clear();
                }
                _playerStats.Clear();
            });
        }
    }
}
