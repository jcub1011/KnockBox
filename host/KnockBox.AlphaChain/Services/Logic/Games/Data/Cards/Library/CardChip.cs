using System.Collections.Immutable;

namespace KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library
{
    /// <summary>
    /// A single glanceable chip shown on a card: a short <paramref name="Label"/> and the CSS color it
    /// renders in. <paramref name="Color"/> is a CSS value (typically a <c>var(--…)</c> token the theme
    /// defines), so a card names an intent — identity / effect / live / accent — without hard-coding a
    /// hex. The standard tokens and factories live on <see cref="CardChips"/>.
    /// </summary>
    public readonly record struct CardChip(string Label, string Color);

    /// <summary>The standard chip color tokens and factory helpers shared by every card.</summary>
    public static class CardChips
    {
        /// <summary>The additive/multiplicative accent — a magnitude chip carries add (green) vs
        /// multiply (orange) in its color, so no separate ADD/MULT chip is needed.</summary>
        public const string Accent = "var(--gc-accent)";

        /// <summary>Neutral violet for a scoring-inert ("FX") card, so it reads as an effect, not a multiplier.</summary>
        public const string Effect = "var(--ac-violet, #b97bff)";

        /// <summary>Cyan for a live, per-player status value (e.g. the Titanium Mirror's shield).</summary>
        public const string Live = "var(--ac-cyan, #00e5ff)";

        /// <summary>A magnitude chip in the additive/multiplicative accent color, except the scoring-inert
        /// "FX" label reads in the neutral effect color.</summary>
        public static CardChip Magnitude(string label)
            => new(label, string.Equals(label, "FX", StringComparison.Ordinal) ? Effect : Accent);
    }
}
