using KnockBox.Core.Services.Drawing;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading;

namespace KnockBox.Services.Drawing
{
    /// <summary>
    /// Thread-safe singleton that stores SVG drawing content under a randomly generated share
    /// code. Entries expire after <see cref="Ttl"/>. Expired entries are purged on each
    /// <see cref="Store"/> call and, so that abandoned SVG strings (which can be tens of KB
    /// each) don't linger when sharing activity stops, by a low-frequency background sweep.
    /// </summary>
    public sealed class SvgClipboardService : ISvgClipboardService, IDisposable
    {
        private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

        // Background sweep cadence. Cheap relative to the TTL — it just snapshots the key set
        // and drops anything already past its expiry, bounding the post-expiry residency window.
        private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(3);

        // Excludes visually ambiguous characters (I, L, O, U) to make codes easier to read aloud.
        private const string CodeChars = "ABCDEFGHJKMNPQRSTVWXYZ";
        private const int CodeLength = 6;
        private const int MaxGenerationAttempts = 64;

        private sealed record Entry(string Content, DateTimeOffset ExpiresAt);

        private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
        private readonly Timer _sweepTimer;
        private readonly ILogger<SvgClipboardService> _logger;

        public SvgClipboardService(ILogger<SvgClipboardService> logger)
        {
            _logger = logger;
            // Periodic sweep so expired entries are reclaimed even with no further Store calls.
            // The callback runs on the threadpool, where an unhandled exception would be
            // process-fatal, so RunSweep swallows and logs rather than letting it escape.
            _sweepTimer = new Timer(static state => ((SvgClipboardService)state!).RunSweep(),
                this, SweepInterval, SweepInterval);
        }

        /// <inheritdoc />
        public string Store(string svgContent)
        {
            PurgeExpired();

            for (var attempt = 0; attempt < MaxGenerationAttempts; attempt++)
            {
                var code = GenerateCode();
                if (_entries.TryAdd(code, new Entry(svgContent, DateTimeOffset.UtcNow + Ttl)))
                {
                    _logger.LogDebug(
                        "Stored SVG clipboard entry under code [{ShareCode}] ({Length} chars).",
                        code, svgContent.Length);
                    return code;
                }
            }

            // Extremely unlikely — fall back to an overwrite so Store never throws.
            var fallback = GenerateCode();
            _entries[fallback] = new Entry(svgContent, DateTimeOffset.UtcNow + Ttl);
            _logger.LogWarning(
                "Exhausted {Attempts} attempts to generate a unique SVG clipboard code; " +
                "overwriting code [{ShareCode}].",
                MaxGenerationAttempts, fallback);
            return fallback;
        }

        /// <inheritdoc />
        public string? Retrieve(string shareCode)
        {
            var key = shareCode.ToUpperInvariant();
            if (_entries.TryGetValue(key, out var entry))
            {
                if (entry.ExpiresAt > DateTimeOffset.UtcNow)
                {
                    _logger.LogDebug("Retrieved SVG clipboard entry for code [{ShareCode}].", key);
                    return entry.Content;
                }

                _entries.TryRemove(key, out _);
                _logger.LogDebug("SVG clipboard code [{ShareCode}] found but expired; removed.", key);
                return null;
            }

            _logger.LogDebug("SVG clipboard code [{ShareCode}] not found.", key);
            return null;
        }

        private static string GenerateCode()
        {
            Span<char> buffer = stackalloc char[CodeLength];
            for (var i = 0; i < CodeLength; i++)
                buffer[i] = CodeChars[RandomNumberGenerator.GetInt32(CodeChars.Length)];
            return new string(buffer);
        }

        // Drops every entry already past its expiry and returns how many were removed.
        private int PurgeExpired()
        {
            var now = DateTimeOffset.UtcNow;
            var removed = 0;
            // ConcurrentDictionary.Keys returns a point-in-time snapshot, so iterating it
            // while concurrent Store/Retrieve calls modify _entries is safe.
            foreach (var key in _entries.Keys)
            {
                if (_entries.TryGetValue(key, out var entry) && entry.ExpiresAt <= now
                    && _entries.TryRemove(key, out _))
                    removed++;
            }
            return removed;
        }

        // Timer-driven background sweep. Best-effort: the threadpool callback must never let an
        // exception escape (that would be process-fatal), so failures are logged and the next
        // tick retries.
        private void RunSweep()
        {
            try
            {
                var removed = PurgeExpired();
                if (removed > 0)
                    _logger.LogDebug(
                        "Background sweep removed {Count} expired SVG clipboard entr{Suffix}.",
                        removed, removed == 1 ? "y" : "ies");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Background sweep of expired SVG clipboard entries failed; will retry next tick.");
            }
        }

        public void Dispose() => _sweepTimer.Dispose();
    }
}
