using KnockBox.Extensions.Disposable;
using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.Navigation.Games;
using Microsoft.AspNetCore.Authentication;
using System.Collections.Concurrent;

namespace KnockBox.Services.State.Games.Lobbies
{
    public class LobbyUriProvider(IRandomNumberService randomNumberService) : ILobbyUriProvider
    {
        private readonly ConcurrentDictionary<string, string> _lobbyCodeMap = [];

        public string? GetLobbyUri(string lobbyCode)
        {
            if (string.IsNullOrWhiteSpace(lobbyCode)) return null;

            return _lobbyCodeMap.TryGetValue(NormalizeLobbyCode(lobbyCode), out var uri)
                ? uri : null;
        }

        public Result<IDisposable> RegisterLobby(string lobbyCode, GameType gameType, out string lobbyUri)
        {
            lobbyUri = string.Empty;

            if (string.IsNullOrWhiteSpace(lobbyCode))
            {
                return Result.FromError<IDisposable>(new ArgumentException("Lobby code is empty.", nameof(lobbyCode)));
            }
            if (!gameType.TryGetNavigationString(out var navigationString))
                return Result.FromError<IDisposable>(new ArgumentException($"Navigation string attribute not set for game type [{gameType}]."));

            lobbyCode = NormalizeLobbyCode(lobbyCode);

            var bytes = randomNumberService.GetRandomBytes(16, RandomType.Secure);
            string obfuscatedRoomCode = Base64UrlTextEncoder.Encode(bytes);

            lobbyUri = $"/room/{navigationString}/{obfuscatedRoomCode}";
            if (_lobbyCodeMap.TryAdd(lobbyCode, lobbyUri))
            {
                return Result.FromValue<IDisposable>(new DisposableAction(() => _lobbyCodeMap.TryRemove(lobbyCode, out _)));
            }
            else
            {
                return Result.FromError<IDisposable>(new InvalidOperationException($"Lobby code [{lobbyCode}] is already registered."));
            }
        }

        private static string NormalizeLobbyCode(string lobbyCode)
        {
            return lobbyCode.Trim().ToUpperInvariant();
        }
    }
}
