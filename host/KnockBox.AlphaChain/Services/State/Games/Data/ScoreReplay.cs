using KnockBox.AlphaChain.Services.Logic.Scoring;

namespace KnockBox.AlphaChain.Services.State.Games.Data
{
    /// <summary>
    /// The most recent accepted word's scoring trace, published on game state so every client
    /// plays the same score-replay animation (shown once below the submit box). <see cref="Sequence"/>
    /// increments on each new play so the strip can <c>@key</c> off it and replay exactly once per word.
    /// </summary>
    /// <param name="Sequence">Monotonic id; bumped on every accepted play.</param>
    /// <param name="UserId">The submitting player's id (for accent colour).</param>
    /// <param name="DisplayName">The submitting player's display name.</param>
    /// <param name="Breakdown">The per-step scoring trace to animate.</param>
    /// <param name="TaxBounty">
    /// Points each Tax Collector owner collected from this (taxed) word, or 0 when none applied.
    /// Every name in <paramref name="TaxCollectors"/> collected this same amount.
    /// </param>
    /// <param name="TaxCollectors">
    /// Display names of the active opponents who collected the Tax Collector bounty from this taxed
    /// word, or empty when none applied. Drives the "stolen by …" line on the replay strip.
    /// </param>
    /// <param name="Reactions">
    /// Reactions that auto-fired on this submission (Amnesty, and the standings-driven offensive/board
    /// reactions), or empty when none did. Rendered as extra rows on the replay strip.
    /// </param>
    public sealed record ScoreReplay(
        int Sequence,
        string UserId,
        string DisplayName,
        ScoreBreakdown Breakdown,
        int TaxBounty = 0,
        IReadOnlyList<string>? TaxCollectors = null,
        IReadOnlyList<ReactionEvent>? Reactions = null)
    {
        /// <summary>Whether this play has a "stolen by …" line (one or more Tax Collector owners collected).</summary>
        public bool HasSteal => TaxCollectors is { Count: > 0 };

        /// <summary>Whether any reaction fired on this submission.</summary>
        public bool HasReactions => Reactions is { Count: > 0 };

        /// <summary>Whether there is anything to animate: modifier steps to walk, a steal, or a fired
        /// reaction. A clean word over an empty bay with no reactions has none and is skipped.</summary>
        public bool HasAnimation => Breakdown.Steps.Count > 0 || HasSteal || HasReactions;

        /// <summary>Reveal-row count used for the constant-time step pacing: the seed, one per
        /// modifier step, the final score, the steal line (when present), and one per fired reaction.</summary>
        public int AnimationRows =>
            Breakdown.Steps.Count + 2 + (HasSteal ? 1 : 0) + (Reactions?.Count ?? 0);
    }
}
