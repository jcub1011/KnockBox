using KnockBox.AlphaChain.Contracts;

namespace KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library
{
    /// <summary>The standardized accent palette: one CSS color token per <see cref="CardAccent"/>, plus
    /// the family map from a card's <see cref="ModifierId"/>. The color tokens are theme variables so the
    /// concrete hues live in CSS; the map is the single source of truth for which family a card is in.</summary>
    public static class CardAccents
    {
        /// <summary>The CSS color token for an accent (a <c>var(--…)</c> the theme defines).</summary>
        public static string Color(CardAccent accent) => accent switch
        {
            CardAccent.Letter => "var(--ac-accent-letter)",
            CardAccent.Clock => "var(--ac-accent-clock)",
            CardAccent.Economy => "var(--ac-accent-economy)",
            CardAccent.Utility => "var(--ac-accent-utility)",
            _ => "var(--ac-accent-neutral)",
        };

        /// <summary>The standardized family accent for a card id. Grouped to mirror the card library
        /// (letter / clock / economy / utility); an unmapped id reads as <see cref="CardAccent.Neutral"/>.</summary>
        public static CardAccent For(ModifierId id) => id switch
        {
            // ── Letter / word-scoring ────────────────────────────────────────
            ModifierId.TheAnchor or ModifierId.Vanilla or ModifierId.ConsonantCrunch
                or ModifierId.VocalVowels or ModifierId.VowelSurge or ModifierId.TheArchitect
                or ModifierId.BrickLayer or ModifierId.Speedracer or ModifierId.LetterHoarder
                or ModifierId.Sesquipedalian or ModifierId.GutturalRoar or ModifierId.HighRoller
                or ModifierId.PerfectLink or ModifierId.DoubleDown or ModifierId.Catalyst
                or ModifierId.TheBlueprint or ModifierId.TryHard or ModifierId.Forgery
                => CardAccent.Letter,

            // ── Clock ────────────────────────────────────────────────────────
            ModifierId.TheVault or ModifierId.Redline or ModifierId.PanicButton
                or ModifierId.HyperDrive or ModifierId.AnchorChain or ModifierId.HeatSink
                or ModifierId.SlowBurn
                => CardAccent.Clock,

            // ── Economy / aggression ─────────────────────────────────────────
            ModifierId.TaxCollector or ModifierId.IrsAgent or ModifierId.RouletteWheel
                or ModifierId.TollBooth or ModifierId.BaitAndSwitch or ModifierId.FlakCannon
                or ModifierId.BountyHunter or ModifierId.ChronoSyphon or ModifierId.TaxWriteOff
                or ModifierId.BoosterPack or ModifierId.Scavenger
                => CardAccent.Economy,

            // ── Utility / defensive / policy ─────────────────────────────────
            ModifierId.Blindfold or ModifierId.TitaniumMirror or ModifierId.Prism
                or ModifierId.Wildcard or ModifierId.MagnifyingGlass
                => CardAccent.Utility,

            _ => CardAccent.Neutral,
        };
    }
}
