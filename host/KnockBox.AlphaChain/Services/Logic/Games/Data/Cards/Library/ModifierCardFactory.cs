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
                    static (_, _) => 10.0, "+10"),

                ModifierId.Vanilla => new VanillaCard(),

                ModifierId.ConsonantCrunch => new CommonModifier(
                    ModifierId.ConsonantCrunch, "Consonant Crunch",
                    "Adds +2 for every consonant in your word. +3 if the word is 7+ characters.",
                    ModifierType.Additive, Always,
                    static (ctx, self) => ctx.Word.Length < 7 ? 2.0 * self.GetConsonantIndicies(ctx).Count() : 3.0 * self.GetConsonantIndicies(ctx).Count(), "+2-3 / cons"),

                ModifierId.VocalVowels => new CommonModifier(
                    ModifierId.VocalVowels, "Vocal Vowels",
                    "Adds +3 for every vowel in your word. +4 if the word is 7+ characters.",
                    ModifierType.Additive, Always,
                    static (ctx, self) => ctx.Word.Length < 7 ? 3.0 * self.GetVowelIndicies(ctx).Count() : 4.0 * self.GetVowelIndicies(ctx).Count(), "+3-4 / vowel"),

                ModifierId.TheArchitect => new TheArchitectCard(),

                ModifierId.BrickLayer => new BrickLayerCard(),

                ModifierId.Speedracer => new SpeedracerCard(),

                ModifierId.LetterHoarder => new CommonModifier(
                    ModifierId.LetterHoarder, "Letter Hoarder",
                    "Adds +1 for every distinct letter in your word.",
                    ModifierType.Additive, Always,
                    static (ctx, _) => ctx.Word.Distinct().Count(), "+1 / uniq"),

                ModifierId.Sesquipedalian => new SesquipedalianCard(),

                ModifierId.HighRoller => new CommonModifier(
                    ModifierId.HighRoller, "High Roller",
                    "Adds +20 when your word begins with a rare letter — Q, X, Z or J.",
                    ModifierType.Additive,
                    static ctx => ctx.Word.Length > 0 && ctx.Word[0] is 'q' or 'x' or 'z' or 'j',
                    static (_, _) => 20.0, "+20"),

                ModifierId.DoubleDown => new CommonModifier(
                    ModifierId.DoubleDown, "The Double Down",
                    "×2 when your word has repeat letters. No repeat letters? Your score is reduced (×0.5).",
                    ModifierType.Multiplier, Always,
                    // Words are normalized lowercase-alpha, so distinct-vs-length detects any repeat letter.
                    static (ctx, _) => ctx.Word.Distinct().Count() != ctx.Word.Length ? 2.0 : 0.5, "×0.5 – 2"),

                // ── Letter-classification cards ──────────────────────────────────
                ModifierId.VowelSurge => new VowelSurgeCard(),
                ModifierId.GutturalRoar => new GutturalRoarCard(),
                ModifierId.PerfectLink => new PerfectLinkCard(),
                ModifierId.Catalyst => new CatalystCard(),
                ModifierId.TheBlueprint => new TheBlueprintCard(),
                ModifierId.TryHard => new TryHardCard(),
                ModifierId.Forgery => new ForgeryCard(),

                // ── Clock cards ──────────────────────────────────────────────────
                ModifierId.TheVault => new TheVaultCard(),
                ModifierId.Redline => new RedlineCard(),
                ModifierId.PanicButton => new PanicButtonCard(),
                ModifierId.HeatSink => new HeatSinkCard(),
                ModifierId.AnchorChain => new AnchorChainCard(),
                ModifierId.HyperDrive => new HyperDriveCard(),
                ModifierId.SlowBurn => new SlowBurnCard(),

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
                ModifierId.ChronoSyphon => new ChronoSyphonCard(),
                ModifierId.TaxWriteOff => new TaxWriteOffCard(),
                ModifierId.BoosterPack => new BoosterPackCard(),
                ModifierId.Scavenger => new ScavengerCard(),

                _ => new CommonModifier(
                    ModifierId.Unknown, "Unknown", "Unknown",
                    ModifierType.Additive, static _ => false,
                    static (_, _) => 0.0),
            };
        }

        /// <inheritdoc />
        public IEnumerable<RoomServiceDescriptor> AllCardRoomServices()
        {
            foreach (var id in AllDealableIds)
                if (CreateCard(default, id) is IContributesRoomServices contributor)
                    foreach (var descriptor in contributor.GetRoomServices())
                        yield return descriptor;
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
