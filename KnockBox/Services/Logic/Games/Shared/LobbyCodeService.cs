using System.Security.Cryptography;
using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.Filtering;

namespace KnockBox.Services.Logic.Games.Shared
{
    public class LobbyCodeService(IProfanityFilter profanityFilter) : ILobbyCodeService
    {
        private const int CodeLength = 6;
        private const int MaxAttempts = 1024;
        private static readonly char[] AllowedChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray();

        private readonly Lock _lock = new();
        private readonly HashSet<string> _issuedCodes = new(StringComparer.Ordinal);

        public int LobbyCodeLength => CodeLength;

        public async ValueTask<ValueResult<string>> IssueLobbyCodeAsync(CancellationToken ct = default)
        {
            try
            {
                for (var attempt = 0; attempt < MaxAttempts; attempt++)
                {
                    ct.ThrowIfCancellationRequested();

                    var code = GenerateCode();
                    var profanities = await profanityFilter.ExtractProfanitiesAsync(code, ct);
                    if (profanities is not null)
                    {
                        continue;
                    }

                    lock (_lock)
                    {
                        if (_issuedCodes.Add(code)) return code;
                    }
                }

                return ValueResult<string>.FromError("Error occured while generating error code.");
            }
            catch (OperationCanceledException)
            {
                return ValueResult<string>.FromCancellation();
            }
            catch (Exception ex)
            {
                return ValueResult<string>.FromError("Error generating lobby code.", $"Error generating lobby code: {ex}");
            }
        }

        public async ValueTask<Result> ReleaseLobbyCodeAsync(string lobbyCode, CancellationToken ct = default)
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                lock (_lock)
                {
                    if (_issuedCodes.Remove(lobbyCode))
                    {
                        return Result.Success;
                    }
                    else
                    {
                        return Result.FromError("Lobby code was not issued.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return Result.FromCancellation();
            }
            catch (Exception ex)
            {
                return Result.FromError("Unable to release lobby code.", $"Error releasing lobby code: {ex}");
            }
        }

        private static string GenerateCode()
        {
            Span<char> buffer = stackalloc char[CodeLength];
            for (var i = 0; i < CodeLength; i++)
            {
                buffer[i] = AllowedChars[RandomNumberGenerator.GetInt32(AllowedChars.Length)];
            }

            return new string(buffer);
        }
    }
}