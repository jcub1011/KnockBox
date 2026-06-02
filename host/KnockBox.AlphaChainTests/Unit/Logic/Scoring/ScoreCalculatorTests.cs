using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards;
using KnockBox.AlphaChain.Services.Logic.Scoring;

namespace KnockBox.AlphaChain.Tests.Unit.Logic.Scoring
{
    /// <summary>
    /// Unit tests for the deterministic scoring pipeline <c>Score = (L + ΣA) × ΠM</c>,
    /// realised as a left → right walk of the Engine Bay seeded with the word length.
    /// </summary>
    [TestClass]
    public class ScoreCalculatorTests
    {
        private readonly ScoreCalculator _calc = new();

        // Build a context for a word with an explicit vowel/consonant split. The word text
        // only matters for cards that read ctx.Word (e.g. Letter Hoarder); the counts are
        // taken from the real letters when we build via WordContext.Build.
        private static WordContext Ctx(string word, char? banned = null) =>
            WordContext.Build(word, banned);

        // Build a context with turn context for the time-aware (Sprinter, Panic Button) and meta
        // (Hyper-Drive multiplier scale) cards.
        private static WordContext Ctx(
            string word, double remainingSeconds, int shotClock = 12, double multiplierScale = 1.0, char? banned = null) =>
            WordContext.Build(word, banned, remainingSeconds, shotClock, multiplierScale);

        // A trivial additive card of fixed value.
        private static ModifierCard Additive(double value) =>
            new("add", "Add", "", ModifierKind.Additive, _ => true, _ => value);

        // A trivial multiplicative card of fixed factor.
        private static ModifierCard Mult(double factor) =>
            new("mul", "Mul", "", ModifierKind.Multiplicative, _ => true, _ => factor);

        [TestMethod]
        public void EmptyBay_ScoreEqualsLength()
        {
            var ctx = Ctx("cat"); // length 3
            Assert.AreEqual(3, _calc.Calculate(ctx, []));
        }

        [TestMethod]
        public void SingleAdditive_AddsToLength()
        {
            var ctx = Ctx("cat"); // length 3
            Assert.AreEqual(3 + 10, _calc.Calculate(ctx, [Additive(10)]));
        }

        [TestMethod]
        public void SingleMultiplicative_MultipliesLength()
        {
            var ctx = Ctx("cats"); // length 4
            Assert.AreEqual(4 * 3, _calc.Calculate(ctx, [Mult(3)]));
        }

        [TestMethod]
        public void TwoAdditivesThenMultiplicative_StacksThenExplodes()
        {
            var ctx = Ctx("cat"); // length 3
            // (3 + 2 + 5) × 2 = 20
            var bay = new[] { Additive(2), Additive(5), Mult(2) };
            Assert.AreEqual(20, _calc.Calculate(ctx, bay));
        }

        [TestMethod]
        public void MultiplicativeBeforeAdditive_RespectsLeftToRightPipeline()
        {
            var ctx = Ctx("cat"); // length 3
            // Suboptimal order: (3 × 2) + 5 = 11, NOT (3 + 5) × 2 = 16.
            var bay = new[] { Mult(2), Additive(5) };
            Assert.AreEqual(11, _calc.Calculate(ctx, bay));
        }

        [TestMethod]
        public void ConditionalMiss_CardIgnored()
        {
            var ctx = Ctx("cat"); // length 3
            // Trigger never fires → contributes nothing; score is just the length.
            var never = new ModifierCard("x", "X", "", ModifierKind.Multiplicative, _ => false, _ => 100);
            Assert.AreEqual(3, _calc.Calculate(ctx, [never]));
        }

        [TestMethod]
        public void RoundsHalfUpAtTheEnd()
        {
            var ctx = Ctx("cat"); // length 3
            // 3 × 1.5 = 4.5 → rounds half-up (away from zero) to 5.
            Assert.AreEqual(5, _calc.Calculate(ctx, [Mult(1.5)]));
        }

        [TestMethod]
        public void CapsAtMaxWordScore()
        {
            var ctx = Ctx("cat");
            // 3 × 100000 well exceeds the cap.
            Assert.AreEqual(ScoreCalculator.MaxWordScore, _calc.Calculate(ctx, [Mult(100_000)]));
        }

        // ── Vowel Surge specific cases (library card) ───────────────────────

        private static ModifierCard VowelSurge =>
            ModifierLibrary.FindById("vowel-surge")!;

        [TestMethod]
        public void VowelSurge_FiresWhenVowelsExceedConsonants()
        {
            // "aerie": a,e,i,e vowels (4) vs r (1) consonant → vowels > consonants → ×2.
            var ctx = Ctx("aerie"); // length 5
            Assert.AreEqual(5 * 2, _calc.Calculate(ctx, [VowelSurge]));
        }

        [TestMethod]
        public void VowelSurge_SkippedWhenConsonantsDominate()
        {
            // "crypt": 0 vowels, 5 consonants → trigger false → score == length.
            var ctx = Ctx("crypt"); // length 5
            Assert.AreEqual(5, _calc.Calculate(ctx, [VowelSurge]));
        }

        [TestMethod]
        public void TaxCollector_IsInertInOwnWordScoring()
        {
            // Tax Collector is a reactive bounty card (resolved in RoundState), not a normal
            // pipeline modifier — it must never affect the owner's own word, banned letter or not.
            var taxCollector = ModifierLibrary.FindById(ModifierLibrary.TaxCollectorId)!;

            // Banned 'a' present in "cat" → still just the length (no own-word multiplier).
            Assert.AreEqual(3, _calc.Calculate(Ctx("cat", banned: 'a'), [taxCollector]));

            // Banned 'z' absent from "cat" → still just the length.
            Assert.AreEqual(3, _calc.Calculate(Ctx("cat", banned: 'z'), [taxCollector]));
        }

        // ── Exhaustive coverage of every shipped ModifierLibrary card ───────

        private static ModifierCard Card(string id) => ModifierLibrary.FindById(id)!;

        [TestMethod]
        public void Anchor_AddsFlatSix()
        {
            // "cat"(3) + 6 = 9, always.
            Assert.AreEqual(9, _calc.Calculate(Ctx("cat"), [Card("anchor")]));
        }

        [TestMethod]
        public void ConsonantCrunch_AddsTwoPerConsonant()
        {
            // "cat" → c,t consonants (2) → +4 → 3 + 4 = 7.
            Assert.AreEqual(7, _calc.Calculate(Ctx("cat"), [Card("consonant-crunch")]));
        }

        [TestMethod]
        public void Architect_MultipliesWhenEightOrLonger()
        {
            // "elephants" length 9 ≥ 8 → ×2 (buffed from ×1.5) → 9 × 2 = 18.
            Assert.AreEqual(18, _calc.Calculate(Ctx("elephants"), [Card("architect")]));
        }

        [TestMethod]
        public void Architect_SkippedWhenShorterThanEight()
        {
            // "cat" length 3 < 8 → trigger false → score == length.
            Assert.AreEqual(3, _calc.Calculate(Ctx("cat"), [Card("architect")]));
        }

        [TestMethod]
        public void BrickLayer_AddsLengthWhenSixOrLonger()
        {
            // "bridge" length 6 ≥ 6 → +6 → 6 + 6 = 12.
            Assert.AreEqual(12, _calc.Calculate(Ctx("bridge"), [Card("brick-layer")]));
        }

        [TestMethod]
        public void BrickLayer_SkippedWhenShorterThanSix()
        {
            // "cat" length 3 < 6 → trigger false → score == length.
            Assert.AreEqual(3, _calc.Calculate(Ctx("cat"), [Card("brick-layer")]));
        }

        [TestMethod]
        public void Sprinter_ScalesWithSecondsRemaining()
        {
            // "cat" length 3 ≤ 4, 10s left → ×(1 + 0.1×10) = ×2 → 3 × 2 = 6.
            Assert.AreEqual(6, _calc.Calculate(Ctx("cat", remainingSeconds: 10), [Card("sprinter")]));
        }

        [TestMethod]
        public void Sprinter_NoBonusWithNoTimeLeft()
        {
            // 0s left → ×1.0 → just the length.
            Assert.AreEqual(3, _calc.Calculate(Ctx("cat", remainingSeconds: 0), [Card("sprinter")]));
        }

        [TestMethod]
        public void Sprinter_SkippedWhenLongerThanFour()
        {
            // "bridge" length 6 > 4 → trigger false even with time to spare → score == length.
            Assert.AreEqual(6, _calc.Calculate(Ctx("bridge", remainingSeconds: 10), [Card("sprinter")]));
        }

        [TestMethod]
        public void PanicButton_BigMultiplierBeforeFinalTwoSeconds_NormalInside()
        {
            // Submit before the final 2 seconds (≥2s left) → ×2.7 → 3 × 2.7 = 8.1 → 8.
            Assert.AreEqual(8, _calc.Calculate(Ctx("cat", remainingSeconds: 9), [Card("panic-button")]));
            // Inside the final 2 seconds (<2s left) → ×1.35 → 3 × 1.35 = 4.05 → 4.
            Assert.AreEqual(4, _calc.Calculate(Ctx("cat", remainingSeconds: 1), [Card("panic-button")]));
        }

        [TestMethod]
        public void LetterHoarder_AddsOnePerDistinctLetter()
        {
            // "cat" → 3 distinct letters → +3 → 3 + 3 = 6.
            Assert.AreEqual(6, _calc.Calculate(Ctx("cat"), [Card("letter-hoarder")]));
        }

        [TestMethod]
        public void LetterHoarder_CountsDistinctNotTotalLetters()
        {
            // "letter" length 6, distinct = l,e,t,r (4) → +4 → 6 + 4 = 10.
            Assert.AreEqual(10, _calc.Calculate(Ctx("letter"), [Card("letter-hoarder")]));
        }

        // ── Group A: new big-word / linguistic niche cards ──────────────────

        [TestMethod]
        public void Sesquipedalian_TriplesWhenTenOrLonger()
        {
            // "exceedingly" length 11 ≥ 10 → ×3 → 11 × 3 = 33.
            Assert.AreEqual(33, _calc.Calculate(Ctx("exceedingly"), [Card("sesquipedalian")]));
        }

        [TestMethod]
        public void Sesquipedalian_SkippedWhenShorterThanTen()
        {
            // "elephants" length 9 < 10 → trigger false → score == length.
            Assert.AreEqual(9, _calc.Calculate(Ctx("elephants"), [Card("sesquipedalian")]));
        }

        [TestMethod]
        public void GutturalRoar_FiresWhenOnlyVowelsAreAorE()
        {
            // "shred" → vowel 'e' only (no i/o/u) → ×1.5 → 5 × 1.5 = 7.5 → 8 (half-up).
            Assert.AreEqual(8, _calc.Calculate(Ctx("shred"), [Card("guttural-roar")]));
        }

        [TestMethod]
        public void GutturalRoar_FiresWithNoVowelsAtAll()
        {
            // "crwth" (a real word) → no i/o/u (no vowels at all) → ×1.5 → 5 × 1.5 = 7.5 → 8.
            Assert.AreEqual(8, _calc.Calculate(Ctx("crwth"), [Card("guttural-roar")]));
        }

        [TestMethod]
        public void GutturalRoar_SkippedWhenWordContainsIOorU()
        {
            // "bridge" contains 'i' → trigger false → score == length.
            Assert.AreEqual(6, _calc.Calculate(Ctx("bridge"), [Card("guttural-roar")]));
        }

        [TestMethod]
        public void HighRoller_AddsTwentyOnRareStartLetter()
        {
            // "quartz" starts with 'q' → +20 → 6 + 20 = 26.
            Assert.AreEqual(26, _calc.Calculate(Ctx("quartz"), [Card("high-roller")]));
        }

        [TestMethod]
        public void HighRoller_SkippedOnCommonStartLetter()
        {
            // "cat" starts with 'c' → trigger false → score == length.
            Assert.AreEqual(3, _calc.Calculate(Ctx("cat"), [Card("high-roller")]));
        }

        [TestMethod]
        public void PerfectLink_MultipliesWhenEndingInVowel()
        {
            // "aerie" ends in 'e' → ×1.5 → 5 × 1.5 = 7.5 → 8 (half-up).
            Assert.AreEqual(8, _calc.Calculate(Ctx("aerie"), [Card("perfect-link")]));
        }

        [TestMethod]
        public void PerfectLink_SkippedWhenEndingInConsonant()
        {
            // "cat" ends in 't' → trigger false → score == length.
            Assert.AreEqual(3, _calc.Calculate(Ctx("cat"), [Card("perfect-link")]));
        }

        // ── Hyper-Drive multiplier scale (meta effect) ──────────────────────

        [TestMethod]
        public void MultiplierScale_DoublesEveryMultiplicativeFactor()
        {
            // scale 2 turns a ×3 card into an effective ×6; additives are unaffected.
            var ctx = Ctx("cats", remainingSeconds: 0, multiplierScale: 2.0); // length 4
            Assert.AreEqual(4 * 6, _calc.Calculate(ctx, [Mult(3)]));
        }

        [TestMethod]
        public void MultiplierScale_LeavesAdditivesUntouched()
        {
            var ctx = Ctx("cat", remainingSeconds: 0, multiplierScale: 2.0); // length 3
            // Additive +10 is unscaled; only multiplicative factors scale.
            Assert.AreEqual(13, _calc.Calculate(ctx, [Additive(10)]));
        }

        [TestMethod]
        public void EveryLibraryCard_NeverThrows_AndStaysWithinCap()
        {
            // Smoke-test the whole catalogue against a couple of words, ensuring each card's
            // Trigger/Value delegate is callable and the result is always a sane, capped int.
            foreach (var card in ModifierLibrary.All)
            {
                foreach (var word in new[] { "cat", "bridge", "elephants", "aerie" })
                {
                    int score = _calc.Calculate(Ctx(word, banned: 'a'), [card]);
                    Assert.IsTrue(score >= 0 && score <= ScoreCalculator.MaxWordScore,
                        $"Card [{card.Id}] on '{word}' produced out-of-range score {score}.");
                }
            }
        }

        // ── New archetype cards (Glass Cannon / Aggro / Shield / Utility) ───

        [TestMethod]
        public void Blindfold_MultipliesByOnePointEight()
        {
            // "cat"(3) × 1.8 = 5.4 → 5 (half-up). The input-hiding is presentational; only the ×1.8 scores.
            Assert.AreEqual(5, _calc.Calculate(Ctx("cat"), [Card("blindfold")]));
        }

        [TestMethod]
        public void DoubleDown_DoublesWithADoubleLetter_HalvesWithout()
        {
            var dd = Card("double-down");
            Assert.AreEqual(12, _calc.Calculate(Ctx("coffin"), [dd]), "'ff' double → ×2 → 6 × 2.");
            Assert.AreEqual(2, _calc.Calculate(Ctx("cat"), [dd]), "no double → ×0.5 → 3 × 0.5 = 1.5 → 2.");
        }

        [TestMethod]
        public void AnchorChain_MultipliesByHalfPerLetter()
        {
            var ac = Card("anchor-chain");
            Assert.AreEqual(18, _calc.Calculate(Ctx("bridge"), [ac]), "6 × (0.5 × 6 = 3).");
            Assert.AreEqual(5, _calc.Calculate(Ctx("cat"), [ac]), "3 × (0.5 × 3 = 1.5) = 4.5 → 5.");
        }

        [TestMethod]
        public void FlakCannon_IsScoreNeutralInThePipeline()
        {
            // Flak Cannon grants 0 points; its auto time-shave is resolved in RoundState. In the
            // scoring pipeline it is an additive +0, so the word just scores its length.
            Assert.AreEqual(3, _calc.Calculate(Ctx("cat"), [Card("flak-cannon")]));
        }

        [TestMethod]
        public void TitaniumMirror_UsesItsLiveShieldMultiplierAsTheScoringFactor()
        {
            var mirror = Card("titanium-mirror");
            // Fresh shield 1.0 → ×1.0 → length unchanged.
            var full = WordContext.Build("bridge", null, 0, 12, 1.0, shieldMultiplier: 1.0);
            Assert.AreEqual(6, _calc.Calculate(full, [mirror]));
            // Decayed into a burden (0.5) → ×0.5 → 6 × 0.5 = 3.
            var decayed = WordContext.Build("bridge", null, 0, 12, 1.0, shieldMultiplier: 0.5);
            Assert.AreEqual(3, _calc.Calculate(decayed, [mirror]));
        }

        [TestMethod]
        public void ZeroPointUtilityCards_LeaveScoreAtTheWordLength()
        {
            // Heat Sink, Prism, Wildcard, Catalyst, Bounty Hunter and Tracer Round are ×1.0
            // placeholders in the pipeline — their power lives in side effects, not scoring.
            foreach (var id in new[] { "heat-sink", "prism", "wildcard", "catalyst", "bounty-hunter", "tracer-round" })
                Assert.AreEqual(6, _calc.Calculate(Ctx("bridge"), [Card(id)]), $"{id} should be score-neutral.");
        }

        // ── WordContext shape (double letters + The Catalyst's Y/W/H ambiguity) ──

        [TestMethod]
        public void HasDoubleLetter_DetectsAdjacentDuplicates()
        {
            Assert.IsTrue(WordContext.Build("coffin", null).HasDoubleLetter, "'ff' is a double letter.");
            Assert.IsTrue(WordContext.Build("apple", null).HasDoubleLetter, "'pp' is a double letter.");
            Assert.IsFalse(WordContext.Build("cat", null).HasDoubleLetter, "no adjacent duplicates.");
        }

        [TestMethod]
        public void Catalyst_CountsYWHasBothVowelAndConsonant()
        {
            var plain = WordContext.Build("why", null, 0, 12, 1.0);
            Assert.AreEqual(0, plain.Word.Count(plain.IsVowel), "Y/W/H are plain consonants without The Catalyst.");
            Assert.AreEqual(3, plain.Word.Count(plain.IsConsonant));

            var catalyst = WordContext.Build("why", null, 0, 12, 1.0, catalyst: true);
            Assert.AreEqual(3, catalyst.Word.Count(catalyst.IsVowel), "The Catalyst counts Y/W/H as vowels too…");
            Assert.AreEqual(3, catalyst.Word.Count(catalyst.IsConsonant), "…and as consonants simultaneously.");
        }

        // A context with The Catalyst active (Y/W/H count as vowels for trigger evaluation).
        private static WordContext Catalyst(string word) =>
            WordContext.Build(word, null, 0, 12, 1.0, catalyst: true);

        [TestMethod]
        public void PerfectLink_Catalyst_FiresWhenEndingInY()
        {
            // "happy" ends in 'y': a plain consonant → skip → score == length 5.
            Assert.AreEqual(5, _calc.Calculate(Ctx("happy"), [Card("perfect-link")]));
            // With The Catalyst, 'y' is a vowel → ×1.5 → 5 × 1.5 = 7.5 → 8 (half-up).
            Assert.AreEqual(8, _calc.Calculate(Catalyst("happy"), [Card("perfect-link")]));
        }

        [TestMethod]
        public void GutturalRoar_Catalyst_FlipsOffWhenYWBecomeVowels()
        {
            // "way" plain: only vowel is 'a' → all vowels a/e → ×1.5 → 3 × 1.5 = 4.5 → 5.
            Assert.AreEqual(5, _calc.Calculate(Ctx("way"), [Card("guttural-roar")]));
            // With The Catalyst, 'w' and 'y' are vowels too → not all a/e → skip → score == length 3.
            Assert.AreEqual(3, _calc.Calculate(Catalyst("way"), [Card("guttural-roar")]));
        }

        [TestMethod]
        public void VowelSurge_Catalyst_FiresWhenAmbiguousLettersTipTheVowelCount()
        {
            // "way" plain: vowels 1 (a) ≤ consonants 2 (w,y) → skip → score == length 3.
            Assert.AreEqual(3, _calc.Calculate(Ctx("way"), [Card("vowel-surge")]));
            // With The Catalyst: vowels 3 (w,a,y) > consonants 2 (w,y) → ×2 → 3 × 2 = 6.
            Assert.AreEqual(6, _calc.Calculate(Catalyst("way"), [Card("vowel-surge")]));
        }

        [TestMethod]
        public void ConsonantCrunch_Catalyst_LeavesConsonantScoringUnchanged()
        {
            // Consonant detection ignores The Catalyst, so the +2-per-consonant bonus is identical
            // with or without it. "why" → w,h,y are consonants (3) → +6 → 3 + 6 = 9, both ways.
            Assert.AreEqual(9, _calc.Calculate(Ctx("why"), [Card("consonant-crunch")]));
            Assert.AreEqual(9, _calc.Calculate(Catalyst("why"), [Card("consonant-crunch")]));
        }

        // ── CalculateSteps (per-step trace for the score-replay animation) ──────

        [TestMethod]
        public void Steps_FinalBeforeTax_EqualsCalculate()
        {
            var ctx = Ctx("cat"); // length 3
            var bay = new[] { Additive(2), Additive(5), Mult(2) }; // (3+2+5)×2 = 20
            var breakdown = _calc.CalculateSteps(ctx, bay, taxed: false);

            Assert.AreEqual(_calc.Calculate(ctx, bay), breakdown.FinalBeforeTax);
            Assert.AreEqual(20, breakdown.FinalBeforeTax);
            Assert.AreEqual(20, breakdown.FinalScore);
            Assert.IsFalse(breakdown.Taxed);
        }

        [TestMethod]
        public void Steps_CaptureRunningTotalsPerCard()
        {
            var ctx = Ctx("cat"); // seed 3
            var bay = new[] { Additive(2), Additive(5), Mult(2) };
            var breakdown = _calc.CalculateSteps(ctx, bay, taxed: false);

            Assert.AreEqual(3, breakdown.Seed);
            Assert.AreEqual(3, breakdown.Steps.Count);
            Assert.AreEqual(5, breakdown.Steps[0].RunningScore);  // 3 + 2
            Assert.AreEqual(10, breakdown.Steps[1].RunningScore); // 5 + 5
            Assert.AreEqual(20, breakdown.Steps[2].RunningScore); // 10 × 2
            Assert.AreEqual("+2", breakdown.Steps[0].ValueText);
            Assert.AreEqual("×2", breakdown.Steps[2].ValueText);
        }

        [TestMethod]
        public void Steps_SkippedCard_MarkedNotTriggered_AndLeavesRunningUnchanged()
        {
            var ctx = Ctx("cat"); // seed 3, "crypt"-style skip via Architect (needs ≥8)
            var bay = new[] { Card("architect") }; // length 3 < 8 → skipped
            var breakdown = _calc.CalculateSteps(ctx, bay, taxed: false);

            Assert.IsFalse(breakdown.Steps[0].Triggered);
            Assert.AreEqual(3, breakdown.Steps[0].RunningScore);
            Assert.AreEqual(3, breakdown.FinalBeforeTax);
        }

        [TestMethod]
        public void Steps_Taxed_ZeroesFinalScoreButKeepsBreakdown()
        {
            var ctx = Ctx("cat"); // seed 3
            var bay = new[] { Additive(10) }; // would be 13
            var breakdown = _calc.CalculateSteps(ctx, bay, taxed: true);

            Assert.IsTrue(breakdown.Taxed);
            Assert.AreEqual(13, breakdown.FinalBeforeTax);
            Assert.AreEqual(0, breakdown.FinalScore);
            Assert.AreEqual(1, breakdown.Steps.Count, "the pipeline trace is still captured when taxed");
        }

        [TestMethod]
        public void Steps_MultiplicativeValueText_FormatsWithoutTrailingZeros()
        {
            var ctx = Ctx("cat");
            var breakdown = _calc.CalculateSteps(ctx, [Mult(1.5)], taxed: false);
            Assert.AreEqual("×1.5", breakdown.Steps[0].ValueText);
        }
    }
}
