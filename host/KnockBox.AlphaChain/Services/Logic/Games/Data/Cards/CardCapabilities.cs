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
        /// (Tax Collector). The owner collects a cut of the would-be score.</summary>
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

    /// <summary>
    /// The Anchor Chain: pins the owner's shot clock to a strict, unmodifiable
    /// <paramref name="Seconds"/> for the whole era. Resolved in
    /// <c>AlphaChainGameState.ComputeArmedShotClockSeconds</c>, where it overrides the base clock
    /// and short-circuits every <see cref="ClockEffect"/> and Hyper-Drive override — the trade-off
    /// for its huge per-letter multiplier.
    /// </summary>
    public sealed record ClockOverride(int Seconds);

    /// <summary>Who an <see cref="AutoTimeShaveRule"/> automatically fires its time-shave at.</summary>
    public enum AutoTimeShaveTarget
    {
        /// <summary>Every active opponent whose cumulative score is higher than the owner's
        /// (Flak Cannon) — a hands-free catch-up ping at the leaders.</summary>
        HigherScore,

        /// <summary>Every active opponent who has played a double-letter word this era
        /// (Scattershot) — punishes the "Apple"/"coffin" crowd.</summary>
        PlayedDoubleLetterThisEra,
    }

    /// <summary>
    /// A hands-free offensive time-shave a modifier fires at the end of its owner's turn: each
    /// opponent matching <paramref name="Target"/> has <paramref name="Seconds"/> queued off their
    /// next shot clock (Flak Cannon, Scattershot). Resolved in <c>RoundState</c> after scoring and
    /// routed through each victim's Titanium Mirror. No manual targeting — pure rule-driven PvP.
    /// </summary>
    public sealed record AutoTimeShaveRule(int Seconds, AutoTimeShaveTarget Target);

    /// <summary>
    /// The Bounty Hunter: the player marked as the round's leader (snapshot at round start) loses a
    /// flat <paramref name="Penalty"/> points if they submit a word shorter than
    /// <paramref name="MinLength"/> letters. Resolved in <c>RoundState</c> on the leader's submission
    /// and routed through their Titanium Mirror (a reflected drain hits the bounty's owner instead).
    /// </summary>
    public sealed record LeaderPenaltyRule(int MinLength, int Penalty);

    /// <summary>
    /// The Titanium Mirror: a shield whose multiplier starts at <paramref name="Start"/> (a passive
    /// ×1.0 placeholder) and drops by <paramref name="DecayPerBlock"/> each time it blocks and
    /// reflects an incoming automated attack (time-shave, point-drain, letter-hijack). The live
    /// multiplier lives on <c>AlphaChainPlayerState.ShieldMultiplier</c> (reset each era) and feeds
    /// the card's scoring factor via <see cref="WordContext.ShieldMultiplier"/>; enough deflections
    /// decay it below 1.0 into a scoring burden carried until the next Intermission.
    /// </summary>
    public sealed record ShieldRule(double Start = 1.0, double DecayPerBlock = 0.1);
}
