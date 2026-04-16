namespace KnockBox.Core.Extensions.Exceptions
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

    /// <summary>
    /// An exception with a user facing component.
    /// </summary>
    public class FormattedException : Exception
    {
        /// <summary>
        /// The message to display to the user.
        /// </summary>
        public string PublicMessage { get; protected set; }

        /// <summary>
        /// Sets both the public and normal message to the provided message.
        /// </summary>
        /// <param name="sharedMessage"></param>
        public FormattedException(string sharedMessage)
            : base(sharedMessage)
        {
            PublicMessage = sharedMessage;
        }

        /// <summary>
        /// Sets the public and normal message separately.
        /// </summary>
        /// <param name="publicMessage"></param>
        /// <param name="message"></param>
        public FormattedException(string publicMessage, string message)
            : base(message)
        {
            PublicMessage = publicMessage;
        }

        /// <summary>
        /// Sets the public message and uses the inner exception for the normal message.
        /// </summary>
        /// <param name="publicMessage"></param>
        /// <param name="innerException"></param>
        public FormattedException(string publicMessage, Exception innerException)
            : base(innerException.Message, innerException)
        {
            PublicMessage = publicMessage;
        }
    }
}
