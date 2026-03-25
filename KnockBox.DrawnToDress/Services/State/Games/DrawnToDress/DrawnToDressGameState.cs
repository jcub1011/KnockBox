using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;

namespace KnockBox.Services.State.Games.DrawnToDress
{
    public class DrawnToDressGameState(
        User host,
        ILogger<DrawnToDressGameState> logger)
        : AbstractGameState(host, logger)
    {
        public GamePhase Phase { get; private set; } = GamePhase.Lobby;

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
