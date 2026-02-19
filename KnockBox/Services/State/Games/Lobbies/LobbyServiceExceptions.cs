namespace KnockBox.Services.State.Games.Lobbies
{
    public class LobbyServiceException(string? message, Exception? inner) 
        : Exception(message, inner) { }

    public class LobbyFullException(int maxPlayerCount) 
        : LobbyServiceException($"The lobby is already at the max player count [{maxPlayerCount}].", null) { }

    public class LobbyNotFoundException(string lobbyCode)
        : LobbyServiceException($"Lobby with code [{lobbyCode}] not found.", null) { }


}
