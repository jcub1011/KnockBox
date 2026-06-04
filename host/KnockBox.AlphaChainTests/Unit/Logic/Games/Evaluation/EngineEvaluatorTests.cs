using System.Collections.Immutable;
using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;
using KnockBox.AlphaChain.Services.Logic.Games.Evaluation;
using KnockBox.AlphaChain.Services.State.Games.Data;

namespace KnockBox.AlphaChain.Tests.Unit.Logic.Games.Evaluation
{
    /// <summary>
    /// Unit tests for the new sequential scoring driver. Each triggered <see cref="IModifierCard"/>
    /// folds itself into the running value left → right; capability cards (The Catalyst, Hyper-Drive,
    /// The Titanium Mirror) are exercised in isolation — only possible now that cards are
    /// self-contained. These also pin scoring parity with the legacy <c>ScoreCalculatorTests</c>.
    /// </summary>
    [TestClass]
    public class EngineEvaluatorTests
    {
        private readonly EngineEvaluator _eval = new();
        private static readonly ModifierCardFactory Factory = new();

        private static IModifierCard Card(ModifierId id) => Factory.CreateCard(default, id);

        // Trivial test doubles.
        private static CommonModifier Additive(double value) => new(
            ModifierId.Unknown, "Add", "", ModifierType.Additive,
            static _ => true, (_, _) => value);

        private static CommonModifier Mult(double factor) => new(
            ModifierId.Unknown, "Mul", "", ModifierType.Multiplier,
            static _ => true, (_, _) => factor);

        // Builds an evaluation context. A player (index 0) is always present so shield/Hyper-Drive
        // capability cards have an owner to read; pass a stub services provider (see Services) when a
        // card needs to read its room state (the shield multiplier, the Hyper-Drive latch).
        private static EngineEvaluationContext Ctx(
            string word, IReadOnlyList<IModifierCard> bay,
            double remaining = 0, double shotClock = 12, char? banned = null,
            AlphaChainPlayerState? player = null, IServiceProvider? services = null,
            IEnumerable<string>? wordHistory = null)
        {
            player ??= new AlphaChainPlayerState { UserId = Guid.NewGuid() };
            return new EngineEvaluationContext(
                word,
                banned is { } b ? new[] { b } : Array.Empty<char>(),
                new[] { player })
            {
                Bay = bay,
                Services = services,
                PlayerIndex = 0,
                RemainingShotClockDuration = remaining,
                ShotClockDuration = shotClock,
                WordHistory = wordHistory?.ToImmutableList() ?? ImmutableList<string>.Empty,
            };
        }

        // A stub room-state provider: card state now lives in services, so the evaluator tests inject
        // a fixed shield reading instead of setting fields on the player.
        private static IServiceProvider Services(double? shield = null)
            => new StubServices(shield);

        private sealed class StubServices(double? shield) : IServiceProvider
        {
            public object? GetService(Type serviceType)
            {
                if (serviceType == typeof(IShieldService) && shield is { } s) return new FixedShield(s);
                return null;
            }
        }

        private sealed class FixedShield(double multiplier) : IShieldService
        {
            public double GetMultiplier(AlphaChainPlayerState player) => multiplier;
            public void Decay(AlphaChainPlayerState player, double step) { }
            public void GrantFresh(AlphaChainPlayerState player) { }
        }

        // ── Core pipeline parity ────────────────────────────────────────────

        [TestMethod]
        public void EmptyBay_ScoreEqualsLength()
            => Assert.AreEqual(3, _eval.Calculate(Ctx("cat", [])));

        [TestMethod]
        public void SingleAdditive_AddsToLength()
            => Assert.AreEqual(13, _eval.Calculate(Ctx("cat", [Additive(10)])));

        [TestMethod]
        public void SingleMultiplicative_MultipliesLength()
            => Assert.AreEqual(12, _eval.Calculate(Ctx("cats", [Mult(3)])));

        [TestMethod]
        public void TwoAdditivesThenMultiplicative_StacksThenExplodes()
            => Assert.AreEqual(20, _eval.Calculate(Ctx("cat", [Additive(2), Additive(5), Mult(2)])));

        [TestMethod]
        public void MultiplicativeBeforeAdditive_RespectsLeftToRightPipeline()
            => Assert.AreEqual(11, _eval.Calculate(Ctx("cat", [Mult(2), Additive(5)])));

        [TestMethod]
        public void ConditionalMiss_CardIgnored()
        {
            var never = new CommonModifier(ModifierId.Unknown, "X", "", ModifierType.Multiplier,
                static _ => false, static (_, _) => 100);
            Assert.AreEqual(3, _eval.Calculate(Ctx("cat", [never])));
        }

        [TestMethod]
        public void RoundsHalfUpAtTheEnd()
            => Assert.AreEqual(5, _eval.Calculate(Ctx("cat", [Mult(1.5)])));

        [TestMethod]
        public void CapsAtMaxWordScore()
            => Assert.AreEqual(ModifierMath.MaxWordScore, _eval.Calculate(Ctx("cat", [Mult(100_000)])));

        // ── Representative library cards ────────────────────────────────────

        [TestMethod]
        public void Anchor_AddsFlatTen()
            => Assert.AreEqual(13, _eval.Calculate(Ctx("cat", [Card(ModifierId.TheAnchor)])));

        [TestMethod]
        public void ConsonantCrunch_AddsTwoPerConsonant()
            => Assert.AreEqual(7, _eval.Calculate(Ctx("cat", [Card(ModifierId.ConsonantCrunch)])));

        [TestMethod]
        public void VocalVowels_AddsThreePerVowel()
            // "aerie" has 4 vowels (a,e,i,e) → +12 → 5 + 12 = 17. (Fixed from the legacy bug that counted consonants.)
            => Assert.AreEqual(17, _eval.Calculate(Ctx("aerie", [Card(ModifierId.VocalVowels)])));

        [TestMethod]
        public void Architect_MultipliesWhenEightOrLonger()
        {
            Assert.AreEqual(27, _eval.Calculate(Ctx("elephants", [Card(ModifierId.TheArchitect)]))); // 9 × 3
            Assert.AreEqual(3, _eval.Calculate(Ctx("cat", [Card(ModifierId.TheArchitect)])));
        }

        [TestMethod]
        public void BrickLayer_AddsLengthWhenSixOrLonger()
        {
            Assert.AreEqual(12, _eval.Calculate(Ctx("bridge", [Card(ModifierId.BrickLayer)])));
            Assert.AreEqual(3, _eval.Calculate(Ctx("cat", [Card(ModifierId.BrickLayer)])));
        }

        [TestMethod]
        public void Speedracer_ScalesInverselyWithRemainingFraction_CappedAtHalfLength()
        {
            // "elephants"(9) > 6. half the clock left → 1/0.5 = ×2 → 18. (cap = 9/2 = 4, not hit)
            Assert.AreEqual(18, _eval.Calculate(Ctx("elephants", [Card(ModifierId.Speedracer)], remaining: 6, shotClock: 12)));
            // 1s of 12 left → 1/(1/12) = ×12, capped at 9/2 = ×4 → 36.
            Assert.AreEqual(36, _eval.Calculate(Ctx("elephants", [Card(ModifierId.Speedracer)], remaining: 1, shotClock: 12)));
            // length ≤ 6 → skipped.
            Assert.AreEqual(6, _eval.Calculate(Ctx("bridge", [Card(ModifierId.Speedracer)], remaining: 1, shotClock: 12)));
        }

        [TestMethod]
        public void LetterHoarder_AddsOnePerDistinctLetter()
            => Assert.AreEqual(10, _eval.Calculate(Ctx("letter", [Card(ModifierId.LetterHoarder)])));

        [TestMethod]
        public void Sesquipedalian_TriplesWhenTenOrLonger()
        {
            Assert.AreEqual(55, _eval.Calculate(Ctx("exceedingly", [Card(ModifierId.Sesquipedalian)]))); // 11 × 5
            Assert.AreEqual(9, _eval.Calculate(Ctx("elephants", [Card(ModifierId.Sesquipedalian)])));
        }

        [TestMethod]
        public void GutturalRoar_FiresWhenOnlyVowelsAreAorE()
        {
            Assert.AreEqual(8, _eval.Calculate(Ctx("shred", [Card(ModifierId.GutturalRoar)])));
            Assert.AreEqual(6, _eval.Calculate(Ctx("bridge", [Card(ModifierId.GutturalRoar)])));
        }

        [TestMethod]
        public void HighRoller_AddsTwentyOnRareStartLetter()
        {
            Assert.AreEqual(26, _eval.Calculate(Ctx("quartz", [Card(ModifierId.HighRoller)])));
            Assert.AreEqual(3, _eval.Calculate(Ctx("cat", [Card(ModifierId.HighRoller)])));
        }

        [TestMethod]
        public void PerfectLink_MultipliesWhenEndingInVowel()
        {
            Assert.AreEqual(8, _eval.Calculate(Ctx("aerie", [Card(ModifierId.PerfectLink)])));
            Assert.AreEqual(3, _eval.Calculate(Ctx("cat", [Card(ModifierId.PerfectLink)])));
        }

        [TestMethod]
        public void VowelSurge_FiresWhenVowelsExceedConsonants()
        {
            // "aerie": 4 vowels > 1 consonant → ×3 → 5 × 3 = 15.
            Assert.AreEqual(15, _eval.Calculate(Ctx("aerie", [Card(ModifierId.VowelSurge)])));
            Assert.AreEqual(5, _eval.Calculate(Ctx("crypt", [Card(ModifierId.VowelSurge)])));
        }

        [TestMethod]
        public void DoubleDown_DoublesWithARepeatLetter_HalvesWithout()
        {
            // Consecutive repeat ("ff") and a non-consecutive repeat ("banana": a×3, n×2) both trigger ×2.
            Assert.AreEqual(12, _eval.Calculate(Ctx("coffin", [Card(ModifierId.DoubleDown)])));
            Assert.AreEqual(12, _eval.Calculate(Ctx("banana", [Card(ModifierId.DoubleDown)])));
            // All-distinct letters → ×0.5.
            Assert.AreEqual(2, _eval.Calculate(Ctx("cat", [Card(ModifierId.DoubleDown)])));
        }

        [TestMethod]
        public void AnchorChain_MultipliesByHalfPerLetter()
        {
            Assert.AreEqual(18, _eval.Calculate(Ctx("bridge", [Card(ModifierId.AnchorChain)])));
            Assert.AreEqual(5, _eval.Calculate(Ctx("cat", [Card(ModifierId.AnchorChain)])));
        }

        [TestMethod]
        public void Blindfold_MultipliesByOnePointEight()
            => Assert.AreEqual(5, _eval.Calculate(Ctx("cat", [Card(ModifierId.Blindfold)])));

        [TestMethod]
        public void PanicButton_BigMultiplierBeforeDangerZone_NormalInside()
        {
            Assert.AreEqual(8, _eval.Calculate(Ctx("cat", [Card(ModifierId.PanicButton)], remaining: 9)));
            Assert.AreEqual(4, _eval.Calculate(Ctx("cat", [Card(ModifierId.PanicButton)], remaining: 1)));
        }

        [TestMethod]
        public void FlakCannon_IsScoreNeutralInThePipeline()
            => Assert.AreEqual(3, _eval.Calculate(Ctx("cat", [Card(ModifierId.FlakCannon)])));

        [TestMethod]
        public void ZeroPointUtilityCards_LeaveScoreAtTheWordLength()
        {
            foreach (var id in new[]
            {
                ModifierId.HeatSink, ModifierId.Prism, ModifierId.Wildcard, ModifierId.Catalyst,
                ModifierId.BountyHunter, ModifierId.RouletteWheel, ModifierId.TaxCollector,
                ModifierId.TollBooth, ModifierId.IrsAgent, ModifierId.BaitAndSwitch,
            })
            {
                // RouletteWheel is ×1.75; the rest are ×1.0 / inert. None should change a length-6 word
                // except RouletteWheel (6 × 1.75 = 10.5 → 11).
                int expected = id == ModifierId.RouletteWheel ? 11 : 6;
                Assert.AreEqual(expected, _eval.Calculate(Ctx("bridge", [Card(id)])), $"{id}");
            }
        }

        // ── Titanium Mirror: scoring factor is the owner's live shield multiplier ──

        [TestMethod]
        public void TitaniumMirror_UsesLiveShieldMultiplierAsTheFactor()
        {
            // No shield service → the card falls back to a passive ×1.0.
            Assert.AreEqual(6, _eval.Calculate(Ctx("bridge", [Card(ModifierId.TitaniumMirror)], services: Services(shield: 1.0))));

            Assert.AreEqual(3, _eval.Calculate(Ctx("bridge", [Card(ModifierId.TitaniumMirror)], services: Services(shield: 0.5))));
        }

        // ── Hyper-Drive: positional ×1.5 on a word longer than 6, only for cards to its right ──

        [TestMethod]
        public void HyperDrive_BoostsOnlyCardsPlacedAfterIt_WhenWordOverSix()
        {
            // "elephant" (8 > 6): Hyper-Drive folds ×1.5 at its slot, so only the later +10 compounds.
            // [HyperDrive, +10]: (8 × 1.5) + 10 = 22.
            Assert.AreEqual(22, _eval.Calculate(Ctx("elephant", [Card(ModifierId.HyperDrive), Additive(10)])));
            // [+10, HyperDrive]: (8 + 10) × 1.5 = 27 — the additive is to the LEFT, so it is boosted.
            Assert.AreEqual(27, _eval.Calculate(Ctx("elephant", [Additive(10), Card(ModifierId.HyperDrive)])));
        }

        [TestMethod]
        public void HyperDrive_DoesNotTrigger_WhenWordSixOrShorter()
            // "bridge" (6, not > 6): Hyper-Drive skips, the later ×3 multiplies the bare length → 18.
            => Assert.AreEqual(18, _eval.Calculate(Ctx("bridge", [Card(ModifierId.HyperDrive), Mult(3)])));

        // ── The Catalyst: capability-interface idiom, now position-dependent ──

        [TestMethod]
        public void Catalyst_AffectsCardsPlacedAfterIt_VowelSurge()
        {
            // "way": plain vowels 1 (a) ≤ consonants 2 (w,y) → VowelSurge skips → length 3.
            Assert.AreEqual(3, _eval.Calculate(Ctx("way", [Card(ModifierId.VowelSurge)])));
            // Catalyst BEFORE Vowel Surge: w,a,y all count as vowels (3) > consonants (w,y = 2) → ×3 → 9.
            Assert.AreEqual(9, _eval.Calculate(Ctx("way", [Card(ModifierId.Catalyst), Card(ModifierId.VowelSurge)])));
        }

        [TestMethod]
        public void Catalyst_PlacedAfterACard_DoesNotAffectIt()
        {
            // Catalyst AFTER Vowel Surge: the surge is evaluated first, before the checker override
            // applies, so it still sees plain classification and skips → length 3.
            Assert.AreEqual(3, _eval.Calculate(Ctx("way", [Card(ModifierId.VowelSurge), Card(ModifierId.Catalyst)])));
        }

        [TestMethod]
        public void Catalyst_PerfectLink_FiresWhenEndingInY()
        {
            Assert.AreEqual(5, _eval.Calculate(Ctx("happy", [Card(ModifierId.PerfectLink)])));
            Assert.AreEqual(8, _eval.Calculate(Ctx("happy", [Card(ModifierId.Catalyst), Card(ModifierId.PerfectLink)])));
        }

        [TestMethod]
        public void Catalyst_LeavesConsonantScoringUnchanged()
        {
            // Y/W/H stay consonants either way, so Consonant Crunch is identical. "why" → 3 consonants → +6 → 9.
            Assert.AreEqual(9, _eval.Calculate(Ctx("why", [Card(ModifierId.ConsonantCrunch)])));
            Assert.AreEqual(9, _eval.Calculate(Ctx("why", [Card(ModifierId.Catalyst), Card(ModifierId.ConsonantCrunch)])));
        }

        // ── New scoring cards ───────────────────────────────────────────────

        [TestMethod]
        public void Blueprint_AddsThreePerLetter_WhenAtLeastAsLongAsPreviousWord()
        {
            // No prior word → always pays out. "cat"(3): 3 + 3×3 = 12.
            Assert.AreEqual(12, _eval.Calculate(Ctx("cat", [Card(ModifierId.TheBlueprint)])));
            // Previous word longer (8) → 3 < 8 → skip → bare length 3.
            Assert.AreEqual(3, _eval.Calculate(Ctx("cat", [Card(ModifierId.TheBlueprint)], wordHistory: new[] { "elephant" })));
            // Previous word shorter (3), current 6 → fires → 6 + 3×6 = 24.
            Assert.AreEqual(24, _eval.Calculate(Ctx("bridge", [Card(ModifierId.TheBlueprint)], wordHistory: new[] { "cat" })));
        }

        [TestMethod]
        public void TryHard_AddsTenthPerLetterBeyondSix()
        {
            Assert.AreEqual(6, _eval.Calculate(Ctx("bridge", [Card(ModifierId.TryHard)])));      // 6 → no trigger
            Assert.AreEqual(8, _eval.Calculate(Ctx("bridges", [Card(ModifierId.TryHard)])));     // 7 → ×1.1 → 7.7 → 8
            Assert.AreEqual(10, _eval.Calculate(Ctx("elephant", [Card(ModifierId.TryHard)])));   // 8 → ×1.2 → 9.6 → 10
        }

        [TestMethod]
        public void Forgery_DoublesPerceivedLength_ForLaterLengthCardsOnly()
        {
            // [Forgery, Vanilla] "cat": seed stays the real 3; Vanilla perceives 6 → +6 → 9.
            Assert.AreEqual(9, _eval.Calculate(Ctx("cat", [Card(ModifierId.Forgery), Card(ModifierId.Vanilla)])));
            // [Forgery, Architect] "moss"(4): perceived 8 ≥ 8 → ×3 → 4 × 3 = 12.
            Assert.AreEqual(12, _eval.Calculate(Ctx("moss", [Card(ModifierId.Forgery), Card(ModifierId.TheArchitect)])));
        }

        [TestMethod]
        public void Forgery_LeavesPerCharacterCardsAndEarlierCardsUntouched()
        {
            // Consonant Crunch reads actual characters → "cat" has 2 consonants → +4 → 7, with or without Forgery.
            Assert.AreEqual(7, _eval.Calculate(Ctx("cat", [Card(ModifierId.ConsonantCrunch)])));
            Assert.AreEqual(7, _eval.Calculate(Ctx("cat", [Card(ModifierId.Forgery), Card(ModifierId.ConsonantCrunch)])));
            // Vanilla placed BEFORE Forgery perceives the real 3 → +3 → 6.
            Assert.AreEqual(6, _eval.Calculate(Ctx("cat", [Card(ModifierId.Vanilla), Card(ModifierId.Forgery)])));
        }

        [TestMethod]
        public void BoosterPack_AddsTwoPerCardToItsRight()
        {
            // Alone → no cards to the right → +0 → bare length 3.
            Assert.AreEqual(3, _eval.Calculate(Ctx("cat", [Card(ModifierId.BoosterPack)])));
            // Two cards to the right → +4 → 3 + 4 = 7 (the two additives contribute 0).
            Assert.AreEqual(7, _eval.Calculate(Ctx("cat", [Card(ModifierId.BoosterPack), Additive(0), Additive(0)])));
        }

        [TestMethod]
        public void Scavenger_AddsOnePerPriorWordContainingTheStartingLetter()
        {
            // "can" starts with 'c'; prior words "cat" and "car" contain 'c' (not "dog") → +2 → 3 + 2 = 5.
            Assert.AreEqual(5, _eval.Calculate(Ctx("can", [Card(ModifierId.Scavenger)], wordHistory: new[] { "cat", "dog", "car" })));
            // No history → +0 → bare length 3.
            Assert.AreEqual(3, _eval.Calculate(Ctx("can", [Card(ModifierId.Scavenger)])));
        }

        // ── CalculateSteps (score-replay trace) ─────────────────────────────

        [TestMethod]
        public void Steps_CaptureRunningTotalsAndOperatorText()
        {
            var breakdown = _eval.CalculateSteps(Ctx("cat", [Additive(2), Additive(5), Mult(2)]), taxed: false);

            Assert.AreEqual(3, breakdown.Seed);
            Assert.AreEqual(3, breakdown.Steps.Count);
            Assert.AreEqual(5, breakdown.Steps[0].RunningScore);
            Assert.AreEqual(20, breakdown.Steps[2].RunningScore);
            Assert.AreEqual("+2", breakdown.Steps[0].ValueText);
            Assert.AreEqual("×2", breakdown.Steps[2].ValueText);
            Assert.AreEqual(20, breakdown.FinalBeforeTax);
            Assert.AreEqual(20, breakdown.FinalScore);
        }

        [TestMethod]
        public void Steps_Taxed_ZeroesFinalScoreButKeepsTrace()
        {
            var breakdown = _eval.CalculateSteps(Ctx("cat", [Additive(10)]), taxed: true);

            Assert.IsTrue(breakdown.Taxed);
            Assert.AreEqual(13, breakdown.FinalBeforeTax);
            Assert.AreEqual(0, breakdown.FinalScore);
            Assert.AreEqual(1, breakdown.Steps.Count);
        }

        [TestMethod]
        public void Steps_MultiplicativeValueText_FormatsWithoutTrailingZeros()
        {
            var breakdown = _eval.CalculateSteps(Ctx("cat", [Mult(1.5)]), taxed: false);
            Assert.AreEqual("×1.5", breakdown.Steps[0].ValueText);
        }

        // ── Whole-catalogue smoke test ──────────────────────────────────────

        [TestMethod]
        public void EveryDealableCard_NeverThrows_AndStaysWithinCap()
        {
            foreach (var id in ModifierCardFactory.AllDealableIds)
                foreach (var word in new[] { "cat", "bridge", "elephants", "aerie" })
                {
                    var player = new AlphaChainPlayerState { UserId = Guid.NewGuid() };
                    int score = _eval.Calculate(Ctx(word, [Card(id)], remaining: 5, shotClock: 12, banned: 'a', player: player, services: Services(shield: 1.0)));
                    Assert.IsTrue(score >= 0 && score <= ModifierMath.MaxWordScore,
                        $"Card [{id}] on '{word}' produced out-of-range score {score}.");
                }
        }
    }
}
