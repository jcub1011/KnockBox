using KnockBox.LinkedList.Contracts;

namespace KnockBox.LinkedList.Services.Projection
{
    /// <summary>
    /// Maps between the server-authoritative <see cref="LinkedListSettings"/> (which keeps the
    /// start-time <c>HostPlays</c> flag and holds the per-turn clock as a <see cref="System.TimeSpan"/>)
    /// and the wire <see cref="LinkedListSettingsView"/> the client edits/displays. The view carries
    /// only the host-editable knobs; applying it preserves every server-only field via <c>with</c>.
    /// </summary>
    internal static class LinkedListSettingsMapping
    {
        public static LinkedListSettingsView ToView(this LinkedListSettings s) => new()
        {
            ScoringMode = s.ScoringMode,
            PlayerStructure = s.PlayerStructure,
            RejectionCap = s.RejectionCap,
            NoImmediateRepeat = s.NoImmediateRepeat,
            Par = s.Par,
            RoundsPerMatch = s.RoundsPerMatch,
            PerTurnClockSeconds = (int)s.PerTurnClock.TotalSeconds,
            EnableTimers = s.EnableTimers,
        };

        /// <summary>
        /// Returns a copy of <paramref name="s"/> with the host-editable fields replaced from
        /// <paramref name="v"/> (clamped), leaving the server-only <c>HostPlays</c> untouched.
        /// </summary>
        public static LinkedListSettings Apply(this LinkedListSettings s, LinkedListSettingsView v) => s with
        {
            ScoringMode = v.ScoringMode,
            PlayerStructure = v.PlayerStructure,
            RejectionCap = Math.Max(0, v.RejectionCap),
            NoImmediateRepeat = v.NoImmediateRepeat,
            Par = v.Par is int p ? Math.Max(1, p) : null,
            RoundsPerMatch = Math.Max(1, v.RoundsPerMatch),
            PerTurnClock = TimeSpan.FromSeconds(Math.Max(0, v.PerTurnClockSeconds)),
            EnableTimers = v.EnableTimers,
            // HostPlays preserved (not on the view — it's a start-time choice).
        };
    }
}
