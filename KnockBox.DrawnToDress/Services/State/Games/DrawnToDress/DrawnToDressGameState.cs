using KnockBox.Services.State.Games.DrawnToDress.Data;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;
using System.Collections.Concurrent;

namespace KnockBox.Services.State.Games.DrawnToDress
{
    public class DrawnToDressGameState(
        User host,
        ILogger<DrawnToDressGameState> logger)
        : AbstractGameState(host, logger)
    {
        /// <summary>
        /// The current phase of the game.
        /// </summary>
        public GamePhase Phase { get; private set; } = GamePhase.Lobby;

        /// <summary>
        /// Game configuration (tunable values with GDD defaults).
        /// </summary>
        public DrawnToDressConfig Config { get; set; } = new();

        /// <summary>
        /// All player states, keyed by player ID.
        /// </summary>
        public readonly ConcurrentDictionary<string, DrawnToDressPlayerState> GamePlayers = new();

        public void SetPhase(GamePhase phase)
        {
            Phase = phase;
            StateChangedEventManager.Notify();
        }
    }

    public enum GamePhase
    {
        Lobby,
        Drawing,
        OutfitBuilding,
        OutfitCustomization,
        Voting,
        Results,
    }
}
