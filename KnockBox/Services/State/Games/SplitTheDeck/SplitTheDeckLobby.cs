using KnockBox.Services.State.Games.Lobbies;

namespace KnockBox.Services.State.Games.SplitTheDeck
{
    public class SplitTheDeckLobby(string roomCode, ILogger logger) 
        : GameLobby<SplitTheDeckLobby>(roomCode, logger)
    {
        public readonly Guid LobbyId = Guid.NewGuid();
    }
}
