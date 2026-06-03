namespace KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library
{
    /// <summary>
    /// Creates a live <see cref="IModifierCard"/> from its <see cref="ModifierId"/>. This is the one
    /// place a card's full behavior is assembled, so adding a card is "one enum value + one arm here"
    /// (plus the card class, when it needs hooks/capabilities). Simple scoring cards are built inline
    /// as <see cref="CommonModifier"/>; richer cards return their dedicated class.
    /// </summary>
    public class ModifierCardFactory : IModifierCardFactory
    {
        private static readonly Func<EngineEvaluationContext, bool> Always = static _ => true;

        public IModifierCard CreateCard(EngineEvaluationContext context, ModifierId modifier)
        {
            return modifier switch
            {
                // ── Data-defined scoring cards ───────────────────────────────────
                ModifierId.TheAnchor => new CommonModifier(
                    ModifierId.TheAnchor, "The Anchor",
                    "Adds a flat +10 to your word, always.",
                    ModifierType.Additive, Always,
                    static (_, _) => 10.0),

                ModifierId.Vanilla => new CommonModifier(
                    ModifierId.Vanilla, "Vanilla",
                    "Adds +1 for every letter in your word.",
                    ModifierType.Additive, Always,
                    static (ctx, _) => ctx.Word.Length),

                ModifierId.ConsonantCrunch => new CommonModifier(
                    ModifierId.ConsonantCrunch, "Consonant Crunch",
                    "Adds +2 for every consonant in your word.",
                    ModifierType.Additive, Always,
                    static (ctx, self) => 2.0 * self.GetConsonantIndicies(ctx).Count()),

                ModifierId.VocalVowels => new CommonModifier(
                    ModifierId.VocalVowels, "Vocal Vowels",
                    "Adds +3 for every vowel in your word.",
                    ModifierType.Additive, Always,
                    // Counts vowels (the legacy card counted consonants — a bug, fixed here to match its name/description).
                    static (ctx, self) => 3.0 * self.GetVowelIndicies(ctx).Count()),

                ModifierId.TheArchitect => new CommonModifier(
                    ModifierId.TheArchitect, "The Architect",
                    "×2 when your word is 8 letters or longer.",
                    ModifierType.Multiplier,
                    static ctx => ctx.Word.Length >= 8,
                    static (_, _) => 2.0),

                ModifierId.BrickLayer => new CommonModifier(
                    ModifierId.BrickLayer, "Brick Layer",
                    "Adds +1 per letter when your word is 6 letters or longer.",
                    ModifierType.Additive,
                    static ctx => ctx.Word.Length >= 6,
                    static (ctx, _) => ctx.Word.Length),

                ModifierId.Speedracer => new CommonModifier(
                    ModifierId.Speedracer, "Speedracer",
                    "When your word is longer than 4 letters, you get a multiplier (1 / ([remaining time] / [total time])). Max of 2x.",
                    ModifierType.Multiplier,
                    static ctx => ctx.Word.Length > 4,
                    static (ctx, _) => Math.Min(1.0 / (ctx.RemainingShotClockDuration / ctx.ShotClockDuration), 2.0)),

                ModifierId.LetterHoarder => new CommonModifier(
                    ModifierId.LetterHoarder, "Letter Hoarder",
                    "Adds +1 for every distinct letter in your word.",
                    ModifierType.Additive, Always,
                    static (ctx, _) => ctx.Word.Distinct().Count()),

                ModifierId.Sesquipedalian => new CommonModifier(
                    ModifierId.Sesquipedalian, "Sesquipedalian",
                    "×3 when your word is 10 letters or longer. Clamped to the max word score.",
                    ModifierType.Multiplier,
                    static ctx => ctx.Word.Length >= 10,
                    static (_, _) => 3.0),

                ModifierId.HighRoller => new CommonModifier(
                    ModifierId.HighRoller, "High Roller",
                    "Adds +20 when your word begins with a rare letter — Q, X, Z or J.",
                    ModifierType.Additive,
                    static ctx => ctx.Word.Length > 0 && ctx.Word[0] is 'q' or 'x' or 'z' or 'j',
                    static (_, _) => 20.0),

                ModifierId.DoubleDown => new CommonModifier(
                    ModifierId.DoubleDown, "The Double Down",
                    "×2 when your word has repeat letters. No repeat letters? Your score is reduced (×0.5).",
                    ModifierType.Multiplier, Always,
                    // Words are normalized lowercase-alpha, so distinct-vs-length detects any repeat letter.
                    static (ctx, _) => ctx.Word.Distinct().Count() != ctx.Word.Length ? 2.0 : 0.5),

                // ── Letter-classification cards ──────────────────────────────────
                ModifierId.VowelSurge => new VowelSurgeCard(),
                ModifierId.GutturalRoar => new GutturalRoarCard(),
                ModifierId.PerfectLink => new PerfectLinkCard(),
                ModifierId.Catalyst => new CatalystCard(),

                // ── Clock cards ──────────────────────────────────────────────────
                ModifierId.TheVault => new TheVaultCard(),
                ModifierId.Redline => new RedlineCard(),
                ModifierId.PanicButton => new PanicButtonCard(),
                ModifierId.HeatSink => new HeatSinkCard(),
                ModifierId.AnchorChain => new AnchorChainCard(),
                ModifierId.HyperDrive => new HyperDriveCard(),

                // ── Utility / policy cards ───────────────────────────────────────
                ModifierId.Blindfold => new BlindfoldCard(),
                ModifierId.Wildcard => new WildcardCard(),
                ModifierId.IrsAgent => new IrsAgentCard(),
                ModifierId.Prism => new PrismCard(),
                ModifierId.TitaniumMirror => new TitaniumMirrorCard(),

                // ── Economy / aggression cards ───────────────────────────────────
                ModifierId.TaxCollector => new TaxCollectorCard(),
                ModifierId.TollBooth => new TollBoothCard(),
                ModifierId.RouletteWheel => new RouletteWheelCard(),
                ModifierId.BountyHunter => new BountyHunterCard(),
                ModifierId.FlakCannon => new FlakCannonCard(),
                ModifierId.BaitAndSwitch => new BaitAndSwitchCard(),

                _ => new CommonModifier(
                    ModifierId.Unknown, "Unknown", "Unknown",
                    ModifierType.Additive, static _ => false,
                    static (_, _) => 0.0),
            };
        }

        /// <summary>Every dealable card id (the catalogue, minus <see cref="ModifierId.Unknown"/>).</summary>
        public static readonly IReadOnlyList<ModifierId> AllDealableIds =
            Enum.GetValues<ModifierId>().Where(id => id != ModifierId.Unknown).ToArray();

        /// <summary>Cards that act as a shield — a player may hold at most one.</summary>
        public static readonly IReadOnlySet<ModifierId> ShieldIds =
            new HashSet<ModifierId> { ModifierId.TitaniumMirror };

        /// <summary>Cards that roll a personal banned letter at era start.</summary>
        public static readonly IReadOnlySet<ModifierId> RollsPersonalBanIds =
            new HashSet<ModifierId> { ModifierId.RouletteWheel, ModifierId.TollBooth };
    }
}
