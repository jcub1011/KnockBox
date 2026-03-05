using KnockBox.Services.State.Games.DiceSimulator.Data;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;
using System.Collections.Concurrent;

namespace KnockBox.Services.State.Games.DiceSimulator
{
    public class DiceSimulatorGameState(
        User host,
        ILogger<DiceSimulatorGameState> logger)
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

        public void AddRoll(DiceRollEntry entry)
        {
            lock (_rollHistory)
            {
                _rollHistory.Add(entry);
            }
        }
        
        public PlayerStats GetOrAddPlayerStats(string playerId, string playerName)
        {
            return _playerStats.GetOrAdd(playerId, _ => new PlayerStats { PlayerName = playerName });
        }
        
        public void ClearHistory()
        {
            lock (_rollHistory)
            {
                _rollHistory.Clear();
            }
            _playerStats.Clear();
        }
    }
}
