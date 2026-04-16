using KnockBox.Core.Extensions.Returns;

namespace KnockBox.Services.Logic.Games.Shared
{
    /// <summary>
    /// A service used to create unique lobby codes.
    /// </summary>
    public interface ILobbyCodeService
    {
        /// <summary>
        /// The length of lobby codes.
        /// </summary>
        public int LobbyCodeLength { get; }

        /// <summary>
        /// Issues a unique lobby code. Lobby codes are always Upper Invariant and never have whitespace.
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        public ValueTask<ValueResult<string>> IssueLobbyCodeAsync(CancellationToken ct = default);

        /// <summary>
        /// Makes the lobby code available for re-use.
        /// </summary>
        /// <param name="lobbyCode"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public ValueTask<Result> ReleaseLobbyCodeAsync(string lobbyCode, CancellationToken ct = default);
    }
}
