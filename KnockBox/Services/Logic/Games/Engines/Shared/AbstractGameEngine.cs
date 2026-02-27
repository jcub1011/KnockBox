using KnockBox.Services.Logic.Games.Shared;
using KnockBox.Services.State.Games.Lobbies;

namespace KnockBox.Services.Logic.Games.Engines.Shared
{
    public abstract class AbstractGameEngine(
        ILobbyCodeService codeService,
        ILobbyUriProvider uriProvider)
    {

    }
}
