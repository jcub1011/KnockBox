using KnockBox.Core.Services.Drawing;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace KnockBox.Services.Drawing
{
    /// <summary>
    /// Thread-safe singleton that stores SVG drawing content under a randomly generated share
    /// code. Entries expire after <see cref="Ttl"/> and are lazily purged on each
    /// <see cref="Store"/> call.
    /// </summary>
    public sealed class SvgClipboardService : ISvgClipboardService
    {
        private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

        // Excludes visually ambiguous characters (I, L, O, U) to make codes easier to read aloud.
        private const string CodeChars = "ABCDEFGHJKMNPQRSTVWXYZ";
        private const int CodeLength = 6;
        private const int MaxGenerationAttempts = 64;

        private sealed record Entry(string Content, DateTimeOffset ExpiresAt);

        private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

        /// <inheritdoc />
        public string Store(string svgContent)
        {
            PurgeExpired();

            for (var attempt = 0; attempt < MaxGenerationAttempts; attempt++)
            {
                var code = GenerateCode();
                if (_entries.TryAdd(code, new Entry(svgContent, DateTimeOffset.UtcNow + Ttl)))
                    return code;
            }

            // Extremely unlikely — fall back to an overwrite so Store never throws.
            var fallback = GenerateCode();
            _entries[fallback] = new Entry(svgContent, DateTimeOffset.UtcNow + Ttl);
            return fallback;
        }

        /// <inheritdoc />
        public string? Retrieve(string shareCode)
        {
            var key = shareCode.ToUpperInvariant();
            if (_entries.TryGetValue(key, out var entry))
            {
                if (entry.ExpiresAt > DateTimeOffset.UtcNow)
                    return entry.Content;

                _entries.TryRemove(key, out _);
            }

            return null;
        }

        private static string GenerateCode()
        {
            Span<char> buffer = stackalloc char[CodeLength];
            for (var i = 0; i < CodeLength; i++)
                buffer[i] = CodeChars[RandomNumberGenerator.GetInt32(CodeChars.Length)];
            return new string(buffer);
        }

        private void PurgeExpired()
        {
            var now = DateTimeOffset.UtcNow;
            // ConcurrentDictionary.Keys returns a point-in-time snapshot, so iterating it
            // while concurrent Store/Retrieve calls modify _entries is safe.
            foreach (var key in _entries.Keys)
            {
                if (_entries.TryGetValue(key, out var entry) && entry.ExpiresAt <= now)
                    _entries.TryRemove(key, out _);
            }
        }
    }
}
