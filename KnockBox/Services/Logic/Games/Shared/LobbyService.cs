using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.Games.Engines.Shared;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.Navigation.Games;
using KnockBox.Services.State.Games.Lobbies;
using KnockBox.Services.State.Users;
using System.Collections.Concurrent;

namespace KnockBox.Services.Logic.Games.Shared
{
    public class LobbyService(
        ILobbyCodeService lobbyCodeService,
        IRandomNumberService random,
        ILobbyUriProvider uriProvider) : ILobbyService
    {
        private readonly ConcurrentDictionary<string, LobbyRegistration> _lobbies = [];

        public async Task<Result> CloseLobbyAsync(
            User user, 
            LobbyRegistration registration, 
            CancellationToken ct = default)
        {
            if (!_lobbies.TryRemove(registration.LobbyCode, out _))
            {
                return Result.FromError(
                    new KeyNotFoundException($"Unable to find lobby with code [{registration.LobbyCode}]."));
            }

            return Result.Success;
        }

        public async Task<Result<LobbyRegistration>> CreateLobbyAsync(
            User host, 
            GameType gameType, 
            CancellationToken ct = default)
        {
            AbstractGameEngine? engine = gameType switch
            {
                GameType.SplitTheDeck => null,
                _ => null
            };

            if (engine is null)
                return Result.FromError<LobbyRegistration>(new Exception($"Game engine for [{gameType}] not registered."));

            var lobbyCodeResult = await lobbyCodeService.IssueLobbyCodeAsync(ct);
            if (!lobbyCodeResult.TryGetValue(out var lobbyCode))
                return Result.FromError<LobbyRegistration>(lobbyCodeResult.Error);

            var stateResult = await engine.CreateStateAsync(ct);
            if (!stateResult.TryGetValue(out var gameState))
                return Result.FromError<LobbyRegistration>(stateResult.Error);

            var registration = new LobbyRegistration();
            var state = stateResult.Value;
            
        }

        public Task<Result<LobbyRegistration>> JoinLobbyAsync(
            User user, 
            string lobbyCode, 
            CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task<Result> LeaveLobbyAsync(
            User user, 
            LobbyRegistration registration, 
            CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task<Result> ReassignHostAsync(
            User previousHost, 
            User newHost, 
            LobbyRegistration registration, 
            CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }
    }
}
