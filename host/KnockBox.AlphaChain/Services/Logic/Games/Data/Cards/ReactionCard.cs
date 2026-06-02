namespace KnockBox.AlphaChain.Services.Logic.Games.Data.Cards
{
    /// <summary>
    /// The game event that auto-fires a reaction. Unlike the old action cards, reactions are
    /// never played by hand: the FSM watches for these moments and fires a held reaction
    /// automatically (and only when it would actually help). The resolver interprets the
    /// <see cref="ReactionTrigger"/> directly — reactions carry no delegates. Adding a value
    /// here therefore requires a matching firing branch in
    /// <see cref="KnockBox.AlphaChain.Services.Logic.Games.FSM.ReactionResolver"/>; see
    /// <see cref="ReactionLibrary"/> for the full add-a-reaction checklist.
    /// </summary>
    public enum ReactionTrigger
    {
        // ── Defensive: fire on something that happens to the holder ──
        /// <summary>The holder submits a word containing a banned letter → suppress the Zero-Point Tax.</summary>
        Amnesty,
        /// <summary>The holder's turn opens on a rare required start letter → clear the requirement.</summary>
        FreeThrow,
        /// <summary>The holder's shot clock expires → grant extra seconds and keep the turn (once).</summary>
        Overtime,
        /// <summary>The holder drops to last place → draw extra reaction cards.</summary>
        Windfall,

        // ── Offensive: fire on an opponent's action (routed through Riposte) ──
        /// <summary>An opponent who is ahead of the holder posts a long (7+ letter) word → steal a
        /// cut of the points they just earned.</summary>
        TollBooth,
        /// <summary>An opponent overtakes the holder specifically → shave their next clock.</summary>
        Frostbite,
        /// <summary>An opponent takes the overall lead → curse their next word with a personal banned letter.</summary>
        Jinx,

        // ── Special ──
        /// <summary>The holder drops to last place → impose a board-wide banned letter for one round.</summary>
        Censor,
        /// <summary>The holder is targeted by an attack reaction → negate it and reflect it at the caster.</summary>
        Riposte,
        /// <summary>An attacker's reaction is negated by the holder's Riposte → silence the attacker
        /// (lock their word input) for the first seconds of their next turn.</summary>
        FeedbackLoop
    }

    /// <summary>Broad role of a reaction, used for UI tinting and the Riposte/attack routing.</summary>
    public enum ReactionClass
    {
        /// <summary>Protects or benefits the holder only.</summary>
        Defensive,
        /// <summary>Acts against an opponent.</summary>
        Offensive,
        /// <summary>Board-wide or counter effects.</summary>
        Special
    }

    /// <summary>
    /// A single-use reaction card held passively in a player's hand. It auto-fires when its
    /// <see cref="Trigger"/> event happens (and only when beneficial), then is consumed. Like
    /// <see cref="ModifierCard"/> and the old <c>ActionCard</c>, it is identified across the
    /// network by its stable <see cref="Id"/>; the resolver interprets <see cref="Trigger"/>
    /// directly, so reactions carry no delegates.
    /// </summary>
    /// <param name="Id">Stable identifier used to reference the card across the network.</param>
    /// <param name="Name">Display name.</param>
    /// <param name="Description">Human-readable rules text (self-describing; surfaced on the card).</param>
    /// <param name="Trigger">The event that fires this reaction.</param>
    public sealed record ReactionCard(
        string Id,
        string Name,
        string Description,
        ReactionTrigger Trigger)
    {
        /// <summary>Stable icon key resolved by <c>CardIcon</c> to an inline SVG glyph.</summary>
        public string Icon { get; init; } = string.Empty;

        /// <summary>Role of the card, for UI tinting.</summary>
        public ReactionClass Class { get; init; } = ReactionClass.Defensive;

        /// <summary>
        /// True for single-target attacks (Toll Booth, Frostbite, Jinx) — these are routed
        /// through the victim's <see cref="ReactionTrigger.Riposte"/> before they land.
        /// </summary>
        public bool IsAttack { get; init; }
    }
}
