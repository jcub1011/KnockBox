using KnockBox.Extensions.Returns;

namespace KnockBox.Services.Logic.Games.Shared
{
    /// <summary>
    /// A service used to create unique lobby codes.
    /// </summary>
    public interface ILobbyCodeService
    {
        /// <summary>
        /// Issues a unique lobby code. Lobby codes are always Upper Invariant.
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        public ValueTask<Result<string>> IssueLobbyCodeAsync(CancellationToken ct = default);

        /// <summary>
        /// Makes the lobby code available for re-use.
        /// </summary>
        /// <param name="lobbyCode"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public ValueTask<Result> ReleaseLobbyCodeAsync(string lobbyCode, CancellationToken ct = default);
    }
}
