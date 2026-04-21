using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.Spardle.Models;
using Microsoft.Extensions.Logging;

namespace KnockBox.Spardle;

public class SpardleState(User host, ILogger logger) : AbstractGameState(host, logger)
{
    // Settings
    public WordPoolMode WordPoolMode { get; set; } = WordPoolMode.NytStandard;
    public WordOrderMode WordOrderMode { get; set; } = WordOrderMode.RandomNoRepeats;
    public WinConditionMode WinCondition { get; set; } = WinConditionMode.Sprinter;
    public bool HardModeEnabled { get; set; } = false;
    public TimeSpan RoundTimer { get; set; } = TimeSpan.FromMinutes(3);
    public bool AllowDictionaryFallback { get; set; } = true;
    public bool AllowCompoundWords { get; set; } = false;
    public double DifficultyMultiplier { get; set; } = 2.0;
    
    // Dynamic defaults
    public bool WaitForAll { get; set; } = false;
    public bool RevealAnswer { get; set; } = true;

    // Game state
    public int TotalRounds { get; set; } = 3;
    public int CurrentRound { get; set; } = 0;
    public string TargetWord { get; set; } = string.Empty;
    public DateTime? RoundStartTime { get; set; }
    public bool IsRoundActive { get; set; } = false;
    public bool IsGameOver { get; set; } = false;

    // Word lists
    public List<string> CustomWordPool { get; set; } = [];
    public List<string> RoundQueue { get; set; } = [];

    // Player tracking
    public Dictionary<string, PlayerState> PlayerStates { get; } = [];

    public PlayerState GetOrCreatePlayerState(string userId)
    {
        if (!PlayerStates.TryGetValue(userId, out var state))
        {
            state = new PlayerState();
            PlayerStates[userId] = state;
        }
        return state;
    }
}
