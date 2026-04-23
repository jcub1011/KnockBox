using System.Diagnostics.CodeAnalysis;

namespace KnockBox.Core.Components.Shared
{
    /// <summary>
    /// Helpers for parsing lobby URIs of the form <c>/room/{routeIdentifier}/{obfuscatedRoomCode}</c>.
    /// Centralizes the trim/split logic that game lobby pages and room pages otherwise duplicate.
    /// </summary>
    public static class LobbyUriHelper
    {
        /// <summary>
        /// Extracts the trailing obfuscated room-code segment from a lobby URI.
        /// </summary>
        /// <param name="uri">Lobby registration URI, e.g. <c>/room/spardle/abc-def</c>.</param>
        /// <param name="obfuscatedRoomCode">Trailing segment when the URI parses, otherwise <see langword="null"/>.</param>
        /// <returns><see langword="true"/> if a non-empty trailing segment was found.</returns>
        public static bool TryExtractObfuscatedRoomCode(string uri, [NotNullWhen(true)] out string? obfuscatedRoomCode)
        {
            obfuscatedRoomCode = null;
            if (string.IsNullOrWhiteSpace(uri)) return false;

            var split = uri.Trim().Trim('/').Split('/');
            if (split.Length == 0) return false;

            var last = split[^1];
            if (string.IsNullOrEmpty(last)) return false;

            obfuscatedRoomCode = last;
            return true;
        }
    }
}
