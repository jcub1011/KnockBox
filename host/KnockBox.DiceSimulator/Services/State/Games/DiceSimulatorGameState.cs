using KnockBox.DiceSimulator.Services.State.Games.Data;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Games.Shared.Interfaces;
using KnockBox.Core.Services.State.Users;
using System.Collections.Concurrent;

namespace KnockBox.DiceSimulator.Services.State.Games
{
    public class DiceSimulatorGameState(
        User host,
        ILogger<DiceSimulatorGameState> logger)
        : AbstractGameState(host, logger),
          IPlayerTrackedGameState<PlayerStats>
    {
        private readonly List<DiceRollEntry> _rollHistory = new();
        public ConcurrentDictionary<string, PlayerStats> GamePlayers { get; } = new();

        public IReadOnlyList<DiceRollEntry> RollHistory 
        { 
            get 
            {
                lock (_rollHistory)
                {
                    return [.. _rollHistory];
                }
            } 
        }
        
        public IReadOnlyDictionary<string, PlayerStats> PlayerStats => GamePlayers;

        public void AddRoll(DiceRollEntry entry)
        {
            lock (_rollHistory)
            {
                _rollHistory.Add(entry);
            }
        }
        
        public PlayerStats GetOrAddPlayerStats(string playerId, string playerName)
        {
            return GamePlayers.GetOrAdd(playerId, _ => new PlayerStats { PlayerName = playerName });
        }
        
        public void ClearHistory()
        {
            lock (_rollHistory)
            {
                _rollHistory.Clear();
            }
            GamePlayers.Clear();
        }
    }
}
