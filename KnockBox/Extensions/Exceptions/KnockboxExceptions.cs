namespace KnockBox.Extensions.Exceptions
{
    /// <summary>
    /// The base class of knockbox exceptions.
    /// </summary>
    /// <param name="message"></param>
    /// <param name="inner"></param>
    public class KnockboxException(string? message, Exception? inner) 
        : Exception(message, inner) { }

    /// <summary>
    /// Raised when the lobby code is not a valid code.
    /// </summary>
    /// <param name="lobbyCode"></param>
    /// <param name="error"></param>
    public class InvalidLobbyCodeException(string lobbyCode, string error)
        : KnockboxException($"The lobby code [{lobbyCode}] is invalid: {error}", null) { }

    /// <summary>
    /// Raised when a lobby is at the max player count.
    /// </summary>
    /// <param name="maxPlayerCount"></param>
    public class LobbyFullException(int maxPlayerCount) 
        : KnockboxException($"The lobby is already at the max player count [{maxPlayerCount}].", null) { }

    /// <summary>
    /// Raised when a lobby is not found.
    /// </summary>
    /// <param name="lobbyCode"></param>
    public class LobbyNotFoundException(string lobbyCode)
        : KnockboxException($"Lobby with code [{lobbyCode}] not found.", null) { }

    /// <summary>
    /// Raised when a user performs an unauthorized action.
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="action"></param>
    public class UnauthorizedUserException(Guid userId, string action)
        : KnockboxException($"User [{userId}] does not have authorization to perform this action: {action}", null);
}
