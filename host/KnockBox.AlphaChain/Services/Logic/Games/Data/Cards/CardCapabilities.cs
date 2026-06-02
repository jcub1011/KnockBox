namespace KnockBox.AlphaChain.Services.Logic.Games.Data.Cards
{
    /// <summary>
    /// Optional, data-only capability descriptors a <see cref="ModifierCard"/> can declare to
    /// reach beyond the pure scoring pipeline. Each is resolved centrally by the engine at a
    /// well-known lifecycle hook (clock arming, era start, submission), so adding a card with a
    /// new ability is "fill in a catalogue entry + the descriptor(s) it needs" — only a genuinely
    /// new <i>kind</i> of hook requires engine changes. The scoring <c>Trigger</c>/<c>Value</c>
    /// delegates stay pure; these descriptors are what carry side-effecting behavior.
    /// </summary>

    /// <summary>Which opponent submission a <see cref="SiphonRule"/> pays out on.</summary>
    public enum SiphonTrigger
    {
        /// <summary>An opponent's word was zeroed by the match's era banned letter
        /// (Tax Collector, Enforcer). The owner collects a cut of the would-be score.</summary>
        OpponentEraTaxed,

        /// <summary>An opponent's (normally-scored) word contained THIS card owner's personal
        /// card-ban rolled at era start (Smuggler's Toll). The owner is minted a cut of the
        /// points the opponent earned; the opponent keeps their score.</summary>
        OpponentUsedMyCardBan,
    }

    /// <summary>
    /// A reactive payout a modifier grants its owner: when an opponent's submission matches
    /// <paramref name="Trigger"/>, the owner collects <paramref name="Rate"/> × the relevant score.
    /// Resolved in <c>RoundState.PaySiphons</c>; the card stays inert in its owner's own scoring
    /// pipeline (its <c>Trigger</c> is <c>Never</c>).
    /// </summary>
    public sealed record SiphonRule(SiphonTrigger Trigger, double Rate);

    /// <summary>
    /// A permanent per-owner shot-clock change applied whenever the owner's turn arms the clock
    /// (Vault −3s, Redline −10%, Panic −50%). The fraction is applied first, then the flat seconds;
    /// the armed clock is floored at <c>AlphaChainGameState.MinShotClockSeconds</c>. Negative values
    /// shorten the clock — the high-risk "glass cannon" trade-off.
    /// </summary>
    public sealed record ClockEffect(int DeltaSeconds = 0, double DeltaFraction = 0);

    /// <summary>
    /// A submitter-side override of the owner's own Zero-Point Tax (IRS): instead of scoring 0 on a
    /// banned-letter word, the owner scores <paramref name="FlatPoints"/>; when
    /// <paramref name="SuppressesBounty"/> is true, no Tax Collector / siphon pays out from it.
    /// </summary>
    public sealed record OwnTaxRule(int FlatPoints, bool SuppressesBounty);

    /// <summary>
    /// Hyper-Drive: when the owner submits an accepted word faster than
    /// <paramref name="ThresholdSeconds"/>, latch an era-scoped state that overrides their shot
    /// clock to <paramref name="ClockOverrideSeconds"/> and scales every multiplicative card by
    /// <paramref name="MultiplierScale"/> for the rest of the era.
    /// </summary>
    public sealed record HyperdriveRule(double ThresholdSeconds, int ClockOverrideSeconds, double MultiplierScale);
}
