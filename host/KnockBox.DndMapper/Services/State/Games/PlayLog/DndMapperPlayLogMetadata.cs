using KnockBox.Core.Services.State.PlayLog;

namespace KnockBox.DndMapper.Services.State.Games.PlayLog
{
    /// <summary>
    /// Builds the per-user play-log metadata for a DnD Mapper session. Pure and DI-free so
    /// it can be unit-tested directly: it reads the (open-ended, non-terminal)
    /// <see cref="DndMapperGameState"/> at the moment the player leaves and emits a
    /// string→string table the home page renders verbatim. DnD Mapper is a sandbox tool
    /// with no winner, so the keys are session-level summary stats (maps drawn, characters
    /// tracked, dice rolled, table size, and how long the session ran). The
    /// <paramref name="currentUserId"/> is accepted for parity with the other games' helpers
    /// and to keep the call site uniform; the metadata here is shared, not personal.
    /// </summary>
    internal static class DndMapperPlayLogMetadata
    {
        public static IReadOnlyDictionary<string, string> Build(DndMapperGameState state, Guid? currentUserId)
        {
            _ = currentUserId; // accepted for call-site uniformity; this summary is session-level.

            var metadata = new Dictionary<string, string>
            {
                ["Maps"] = state.Maps.Length.ToString(),
                ["Characters"] = state.Sheets.Count.ToString(),
                ["Rolls"] = state.RollLog.Count.ToString(),
            };

            // RosterIncludingHost is the host plus every registered player, which is the
            // closest stand-in for "who was at the table" in a host-driven sandbox tool.
            metadata.Set(StandardMetadata.Players, state.RosterIncludingHost.Length.ToString());
            metadata.Set(StandardMetadata.Duration, FormatDuration(DateTime.UtcNow - state.CreatedAt));

            return metadata;
        }

        // Mirrors the play-log convention of a compact, human-readable elapsed string:
        // sessions under an hour render as mm:ss, longer ones as h:mm:ss. Clamps negative
        // spans (clock skew) to zero.
        private static string FormatDuration(TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero)
                elapsed = TimeSpan.Zero;

            return elapsed.TotalHours >= 1
                ? elapsed.ToString(@"h\:mm\:ss")
                : elapsed.ToString(@"mm\:ss");
        }
    }
}
