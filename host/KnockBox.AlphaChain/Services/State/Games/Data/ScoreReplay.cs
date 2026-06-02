using KnockBox.AlphaChain.Services.Logic.Scoring;

namespace KnockBox.AlphaChain.Services.State.Games.Data
{
    /// <summary>
    /// The most recent accepted word's scoring trace, published on game state so every client
    /// plays the same center-stage score-replay animation. <see cref="Sequence"/> increments on
    /// each new play so the overlay can <c>@key</c> off it and replay exactly once per word.
    /// </summary>
    /// <param name="Sequence">Monotonic id; bumped on every accepted play.</param>
    /// <param name="UserId">The submitting player's id (for accent colour).</param>
    /// <param name="DisplayName">The submitting player's display name.</param>
    /// <param name="Breakdown">The per-step scoring trace to animate.</param>
    public sealed record ScoreReplay(
        int Sequence,
        string UserId,
        string DisplayName,
        ScoreBreakdown Breakdown);
}
