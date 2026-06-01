using System.Collections.Immutable;

namespace KnockBox.AlphaChain.Services.Logic.Games.Data.Cards
{
    /// <summary>
    /// The canonical, immutable catalogue of every modifier card. The <c>Trigger</c>/<c>Value</c>
    /// delegates live here (on this single static list) and are referenced elsewhere only by
    /// <see cref="ModifierCard.Id"/>. The initial set is the GDD's modifiers plus a little
    /// filler for variety; add new cards here and they become draftable everywhere.
    /// </summary>
    public static class ModifierLibrary
    {
        /// <summary>Always-true trigger for unconditional cards.</summary>
        private static readonly Func<WordContext, bool> Always = static _ => true;

        /// <summary>Always-false trigger for cards that never fold into the owner's own scoring
        /// pipeline (their effect is resolved elsewhere — e.g. Tax Collector's reactive bounty).</summary>
        private static readonly Func<WordContext, bool> Never = static _ => false;

        /// <summary>Stable id of the Tax Collector card (resolved by the round's bounty payout).</summary>
        public const string TaxCollectorId = "tax-collector";

        /// <summary>Fraction of an opponent's would-be (taxed-away) word score that each Tax
        /// Collector owner collects.</summary>
        public const double TaxCollectorRate = 0.5;

        /// <summary>Every modifier card, in catalogue order.</summary>
        public static readonly ImmutableArray<ModifierCard> All =
        [
            new ModifierCard(
                "anchor", "The Anchor",
                "Adds a flat +12 to your word, always.",
                ModifierKind.Additive,
                Always,
                static _ => 12),

            new ModifierCard(
                "consonant-crunch", "Consonant Crunch",
                "Adds +2 for every consonant in your word.",
                ModifierKind.Additive,
                Always,
                static ctx => 2 * ctx.Consonants),

            new ModifierCard(
                "vowel-surge", "Vowel Surge",
                "×2 when your word has more vowels than consonants.",
                ModifierKind.Multiplicative,
                static ctx => ctx.Vowels > ctx.Consonants,
                static _ => 2),

            new ModifierCard(
                "architect", "The Architect",
                "×1.5 when your word is 8 letters or longer.",
                ModifierKind.Multiplicative,
                static ctx => ctx.Length >= 8,
                static _ => 1.5),

            new ModifierCard(
                "brick-layer", "Brick Layer",
                "Adds +1 per letter when your word is 6 letters or longer.",
                ModifierKind.Additive,
                static ctx => ctx.Length >= 6,
                static ctx => ctx.Length),

            new ModifierCard(
                "sprinter", "Sprinter",
                "×1.25 when your word is 4 letters or shorter.",
                ModifierKind.Multiplicative,
                static ctx => ctx.Length <= 4,
                static _ => 1.25),

            new ModifierCard(
                "letter-hoarder", "Letter Hoarder",
                "Adds +1 for every distinct letter in your word.",
                ModifierKind.Additive,
                Always,
                static ctx => ctx.Word.Distinct().Count()),

            // Tax Collector does NOT fold into its owner's own scoring pipeline (Trigger is
            // Never). Instead it is a passive bounty: when an *opponent* plays a banned-letter
            // word (Zero-Point Tax), each owner collects half of the points it would have scored.
            // That reactive payout is resolved in RoundState; here the card is inert.
            new ModifierCard(
                TaxCollectorId, "Tax Collector",
                "When an opponent plays a banned-letter word, collect half the points it would have scored.",
                ModifierKind.Multiplicative,
                Never,
                static _ => TaxCollectorRate),
        ];

        /// <summary>Fast id → card lookup for resolving network ids against the catalogue.</summary>
        private static readonly ImmutableDictionary<string, ModifierCard> ById =
            All.ToImmutableDictionary(c => c.Id, StringComparer.Ordinal);

        /// <summary>Resolves a card by its stable id, or null when the id is unknown.</summary>
        public static ModifierCard? FindById(string id) =>
            ById.TryGetValue(id, out var card) ? card : null;
    }
}
