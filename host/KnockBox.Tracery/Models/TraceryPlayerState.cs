using System.Collections.Immutable;

namespace KnockBox.Tracery.Models
{
    /// <summary>
    /// Per-player, per-match data. One instance is created per participant at game start
    /// and lives on <c>TraceryGameState</c> for the match duration. Writes are owned by
    /// the engine and only ever happen inside <c>Execute</c>/<c>ExecuteAsync</c>.
    /// </summary>
    /// <remarks>
    /// Mirrors Spardle's <c>PlayerState</c>: cumulative score persists across rounds while
    /// round-scoped fields are cleared by <see cref="ResetRound"/> at the start of each
    /// round. <see cref="BankedWords"/> is a placeholder this milestone — real tracing,
    /// validation, and scoring arrive in Milestones 05/06.
    /// </remarks>
    public sealed class TraceryPlayerState
    {
        /// <summary>Total score accumulated across all completed rounds.</summary>
        public int CumulativeScore { get; set; }

        /// <summary>Points earned in the most recently completed round (for the results screen).</summary>
        public int LastRoundPoints { get; set; }

        /// <summary>Points earned so far in the current (in-progress) round.</summary>
        public int RoundScore { get; set; }

        /// <summary>Words this player has accepted in the current round. Placeholder until Milestone 05/06.</summary>
        public ImmutableList<string> BankedWords { get; set; } = [];

        /// <summary>Clears the round-scoped fields. Call at the start of each round.</summary>
        public void ResetRound()
        {
            RoundScore = 0;
            LastRoundPoints = 0;
            BankedWords = BankedWords.Clear();
        }
    }
}
