using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;

namespace KnockBox.AlphaChain.Tests.Unit.Support
{
    /// <summary>
    /// Test helper that builds live <see cref="IModifierCard"/>s by the legacy string ids the state
    /// tests use, so those tests keep reading naturally after the card model moved to
    /// <see cref="ModifierId"/> + <see cref="ModifierCardFactory"/>.
    /// </summary>
    public static class TestModifierCards
    {
        private static readonly ModifierCardFactory Factory = new();

        /// <summary>The Tax Collector's legacy string id (was <c>ModifierLibrary.TaxCollectorId</c>).</summary>
        public const string TaxCollectorId = "tax-collector";

        private static readonly Dictionary<string, ModifierId> ByLegacyId = new(StringComparer.Ordinal)
        {
            ["anchor"] = ModifierId.TheAnchor,
            ["vanilla"] = ModifierId.Vanilla,
            ["consonant-crunch"] = ModifierId.ConsonantCrunch,
            ["vocal-vowel"] = ModifierId.VocalVowels,
            ["vowel-surge"] = ModifierId.VowelSurge,
            ["architect"] = ModifierId.TheArchitect,
            ["brick-layer"] = ModifierId.BrickLayer,
            ["speedracer"] = ModifierId.Speedracer,
            ["letter-hoarder"] = ModifierId.LetterHoarder,
            ["sesquipedalian"] = ModifierId.Sesquipedalian,
            ["guttural-roar"] = ModifierId.GutturalRoar,
            ["high-roller"] = ModifierId.HighRoller,
            ["perfect-link"] = ModifierId.PerfectLink,
            ["the-vault"] = ModifierId.TheVault,
            ["redline"] = ModifierId.Redline,
            ["panic-button"] = ModifierId.PanicButton,
            ["hyper-drive"] = ModifierId.HyperDrive,
            ["tax-collector"] = ModifierId.TaxCollector,
            ["irs"] = ModifierId.IrsAgent,
            ["roulette-wheel"] = ModifierId.RouletteWheel,
            ["toll-booth"] = ModifierId.TollBooth,
            ["bait-and-switch"] = ModifierId.BaitAndSwitch,
            ["blindfold"] = ModifierId.Blindfold,
            ["double-down"] = ModifierId.DoubleDown,
            ["anchor-chain"] = ModifierId.AnchorChain,
            ["flak-cannon"] = ModifierId.FlakCannon,
            ["bounty-hunter"] = ModifierId.BountyHunter,
            ["titanium-mirror"] = ModifierId.TitaniumMirror,
            ["heat-sink"] = ModifierId.HeatSink,
            ["prism"] = ModifierId.Prism,
            ["wildcard"] = ModifierId.Wildcard,
            ["catalyst"] = ModifierId.Catalyst,
            ["the-blueprint"] = ModifierId.TheBlueprint,
            ["slow-burn"] = ModifierId.SlowBurn,
            ["try-hard"] = ModifierId.TryHard,
            ["chrono-syphon"] = ModifierId.ChronoSyphon,
            ["forgery"] = ModifierId.Forgery,
            ["tax-write-off"] = ModifierId.TaxWriteOff,
            ["booster-pack"] = ModifierId.BoosterPack,
            ["scavenger"] = ModifierId.Scavenger,
            ["magnifying-glass"] = ModifierId.MagnifyingGlass,
        };

        /// <summary>Maps a legacy string id to its <see cref="ModifierId"/>.</summary>
        public static ModifierId ToId(string legacyId) => ByLegacyId[legacyId];

        /// <summary>Creates a live card from a legacy string id.</summary>
        public static IModifierCard Create(string legacyId) => Factory.CreateCard(default, ToId(legacyId));

        /// <summary>Creates a live card from a strongly-typed id.</summary>
        public static IModifierCard Create(ModifierId id) => Factory.CreateCard(default, id);

        /// <summary>Whether a card is a shield (Titanium Mirror), by either id form.</summary>
        public static bool IsShield(IModifierCard card) => ModifierCardFactory.ShieldIds.Contains(card.GetId());
    }
}
