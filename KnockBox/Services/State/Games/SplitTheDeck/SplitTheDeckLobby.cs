using KnockBox.Services.Logic.Games.Lobbies;
using KnockBox.Services.State.Games.Lobbies;

namespace KnockBox.Services.State.Games.SplitTheDeck
{
    public class SplitTheDeckLobby(ILogger<SplitTheDeckLobby> logger, ILobbyCodeService codeService) 
        : GameLobby<SplitTheDeckLobby>(logger, codeService)
    {
        public readonly Guid LobbyId = Guid.NewGuid();
    }
}
