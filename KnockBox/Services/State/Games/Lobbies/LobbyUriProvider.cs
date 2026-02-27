using KnockBox.Extensions.Disposable;
using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.Navigation.Games;
using Microsoft.AspNetCore.WebUtilities;

namespace KnockBox.Services.State.Games.Lobbies
{
    public class LobbyUriProvider(IRandomNumberService randomNumberService, ILogger<LobbyUriProvider> logger) : ILobbyUriProvider
    {
        private readonly HashSet<string> _activeUris = [];
        private readonly Dictionary<string, string> _registrations = [];
        private readonly Lock _lock = new();

        public string? GetLobbyUri(string lobbyCode)
        {
            if (string.IsNullOrWhiteSpace(lobbyCode)) return null;

            lock (_lock)
            {
                return _registrations.TryGetValue(NormalizeLobbyCode(lobbyCode), out var uri)
                    ? uri : null;
            }
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

            lock (_lock)
            {
                if (_registrations.ContainsKey(lobbyCode))
                    return Result.FromError<IDisposable>(new InvalidOperationException($"Lobby with code [{lobbyCode}] is already registered."));

                int remainingAttempts = 10;
                while (remainingAttempts-- > 0)
                {
                    // Though unlikely, it is possible for secure random to repeat
                    // The loop is to ensure that every uri is unique
                    var bytes = randomNumberService.GetRandomBytes(16, RandomType.Secure);
                    var obfuscatedRoomCode = Base64UrlTextEncoder.Encode(bytes);

                    // Extra variable is required because anonymous functions don't allow referencing in/out parameters
                    var uri = $"/room/{navigationString}/{obfuscatedRoomCode}";
                    lobbyUri = uri;
                    if (_activeUris.Add(lobbyUri))
                    {
                        _registrations[lobbyCode] = uri;

                        return Result.FromValue<IDisposable>(new DisposableAction(() =>
                        {
                            lock (_lock)
                            {
                                _activeUris.Remove(uri);
                                _registrations.Remove(lobbyCode);
                            }
                        }));
                    }
                    else
                    {
                        logger.LogWarning("Generating an obfuscated lobby code for [{lobbyCode}] resulted in a duplicate uri. Buy a lottery ticket!", lobbyCode);
                    }
                }
            }

            logger.LogError("Generating an obfuscated lobby code for [{lobbyCode}] failed 10 consecutive times. Something is fatally wrong.", lobbyCode);
            lobbyUri = string.Empty;
            return Result.FromError<IDisposable>(new InvalidOperationException($"Error generating an obfuscated uri for lobby code [{lobbyCode}]."));
        }

        private static string NormalizeLobbyCode(string lobbyCode)
        {
            return lobbyCode.Trim().ToUpperInvariant();
        }
    }
}
