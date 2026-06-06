using KnockBox.AlphaChain.Services.Logic.Games.Data;

namespace KnockBox.AlphaChain.Tests.Unit.Logic.Games.Data
{
    /// <summary>
    /// Unit tests for <see cref="AlphaChainSettings.Validate"/> — the single source of truth
    /// for a legal match config (shared by the lobby's start-button gating and
    /// <c>StartAsyncCore</c>). Every rule has a positive and a negative case, plus boundary
    /// checks at each named constant.
    /// </summary>
    [TestClass]
    public class AlphaChainSettingsTests
    {
        [TestMethod]
        public void Defaults_AreValid()
        {
            var result = new AlphaChainSettings().Validate();

            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(0, result.Violations.Length);
            Assert.AreEqual(string.Empty, result.Summary);
        }

        // ── Shot clock ──────────────────────────────────────────────────────

        [TestMethod]
        public void ShotClock_BelowMin_IsInvalid()
        {
            var result = new AlphaChainSettings { ShotClockSeconds = AlphaChainSettings.MinShotClockSeconds - 1 }.Validate();
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Summary.Contains("Shot clock"));
        }

        [TestMethod]
        public void ShotClock_AboveMax_IsInvalid()
        {
            var result = new AlphaChainSettings { ShotClockSeconds = AlphaChainSettings.MaxShotClockSeconds + 1 }.Validate();
            Assert.IsFalse(result.IsValid);
        }

        [TestMethod]
        public void ShotClock_AtBoundaries_IsValid()
        {
            Assert.IsTrue(new AlphaChainSettings { ShotClockSeconds = AlphaChainSettings.MinShotClockSeconds }.Validate().IsValid);
            Assert.IsTrue(new AlphaChainSettings { ShotClockSeconds = AlphaChainSettings.MaxShotClockSeconds }.Validate().IsValid);
        }

        // ── Era interval ────────────────────────────────────────────────────

        [TestMethod]
        public void EraInterval_Zero_IsInvalid()
        {
            var result = new AlphaChainSettings { EraInterval = 0 }.Validate();
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Summary.Contains("Era interval"));
        }

        [TestMethod]
        public void EraInterval_AboveMax_IsInvalid()
            => Assert.IsFalse(new AlphaChainSettings { EraInterval = AlphaChainSettings.MaxEraInterval + 1 }.Validate().IsValid);

        [TestMethod]
        public void EraInterval_AtMin_IsValid()
            => Assert.IsTrue(new AlphaChainSettings { EraInterval = AlphaChainSettings.MinEraInterval }.Validate().IsValid);

        // ── Era count ───────────────────────────────────────────────────────

        [TestMethod]
        public void EraCount_Zero_IsInvalid()
        {
            var result = new AlphaChainSettings { EraCount = 0 }.Validate();
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Summary.Contains("Era count"));
        }

        [TestMethod]
        public void EraCount_Negative_IsInvalid()
            => Assert.IsFalse(new AlphaChainSettings { EraCount = -3 }.Validate().IsValid);

        [TestMethod]
        public void EraCount_AtMin_IsValid()
            => Assert.IsTrue(new AlphaChainSettings { EraCount = AlphaChainSettings.MinEraCount }.Validate().IsValid);

        // ── Intermission timer ──────────────────────────────────────────────

        [TestMethod]
        public void IntermissionTimer_BelowMin_IsInvalid()
        {
            var result = new AlphaChainSettings { IntermissionCardSelectSeconds = AlphaChainSettings.MinIntermissionSeconds - 1 }.Validate();
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Summary.Contains("Intermission"));
        }

        [TestMethod]
        public void IntermissionTimer_AtMin_IsValid()
            => Assert.IsTrue(new AlphaChainSettings { IntermissionCardSelectSeconds = AlphaChainSettings.MinIntermissionSeconds }.Validate().IsValid);

        // ── Sniper-ban timer ────────────────────────────────────────────────

        [TestMethod]
        public void SniperBanTimer_BelowMin_IsInvalid()
        {
            var result = new AlphaChainSettings { SniperBanSeconds = AlphaChainSettings.MinSniperBanSeconds - 1 }.Validate();
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Summary.Contains("Sniper"));
        }

        [TestMethod]
        public void SniperBanTimer_AtMin_IsValid()
            => Assert.IsTrue(new AlphaChainSettings { SniperBanSeconds = AlphaChainSettings.MinSniperBanSeconds }.Validate().IsValid);

        // ── Get Ready countdown ─────────────────────────────────────────────

        [TestMethod]
        public void Countdown_DefaultsToFiveSeconds()
            => Assert.AreEqual(5, new AlphaChainSettings().PreRoundCountdownSeconds);

        [TestMethod]
        public void Countdown_BelowMin_IsInvalid()
        {
            var result = new AlphaChainSettings { PreRoundCountdownSeconds = AlphaChainSettings.MinCountdownSeconds - 1 }.Validate();
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Summary.Contains("Get Ready"));
        }

        [TestMethod]
        public void Countdown_AboveMax_IsInvalid()
            => Assert.IsFalse(new AlphaChainSettings { PreRoundCountdownSeconds = AlphaChainSettings.MaxCountdownSeconds + 1 }.Validate().IsValid);

        [TestMethod]
        public void Countdown_AtBoundaries_IsValid()
        {
            Assert.IsTrue(new AlphaChainSettings { PreRoundCountdownSeconds = AlphaChainSettings.MinCountdownSeconds }.Validate().IsValid);
            Assert.IsTrue(new AlphaChainSettings { PreRoundCountdownSeconds = AlphaChainSettings.MaxCountdownSeconds }.Validate().IsValid);
        }

        // ── Cards dealt per era ─────────────────────────────────────────────

        [TestMethod]
        public void ModifiersPerEra_Negative_IsInvalid()
            => Assert.IsFalse(new AlphaChainSettings { ModifiersDealtPerEra = -1 }.Validate().IsValid);

        [TestMethod]
        public void ModifiersPerEra_AboveMax_IsInvalid()
            => Assert.IsFalse(new AlphaChainSettings { ModifiersDealtPerEra = AlphaChainSettings.MaxCardsDealtPerEra + 1 }.Validate().IsValid);

        [TestMethod]
        public void ModifiersPerEra_ZeroIsAllowed()
            => Assert.IsTrue(new AlphaChainSettings { ModifiersDealtPerEra = 0 }.Validate().IsValid);

        // ── Ban mode ────────────────────────────────────────────────────────

        [TestMethod]
        public void BanMode_UndefinedEnumValue_IsInvalid()
        {
            var result = new AlphaChainSettings { BanMode = (BanLetterMode)999 }.Validate();
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Summary.Contains("Ban mode"));
        }

        [TestMethod]
        [DataRow(BanLetterMode.Vowels)]
        [DataRow(BanLetterMode.Consonants)]
        [DataRow(BanLetterMode.All)]
        public void BanMode_EveryDefinedValue_IsValid(BanLetterMode mode)
            => Assert.IsTrue(new AlphaChainSettings { BanMode = mode }.Validate().IsValid);

        // ── HostPlays is not a validated field ───────────────────────────────

        [TestMethod]
        public void HostPlays_DoesNotAffectValidity()
        {
            Assert.IsTrue(new AlphaChainSettings { HostPlays = true }.Validate().IsValid);
            Assert.IsTrue(new AlphaChainSettings { HostPlays = false }.Validate().IsValid);
        }

        // ── EnableTutorials: defaults on, never affects validity ─────────────

        [TestMethod]
        public void EnableTutorials_DefaultsOn()
        {
            Assert.IsTrue(new AlphaChainSettings().EnableTutorials);
        }

        [TestMethod]
        public void EnableTutorials_DoesNotAffectValidity()
        {
            Assert.IsTrue(new AlphaChainSettings { EnableTutorials = true }.Validate().IsValid);
            Assert.IsTrue(new AlphaChainSettings { EnableTutorials = false }.Validate().IsValid);
        }

        // ── Multiple violations are all reported ─────────────────────────────

        [TestMethod]
        public void MultipleViolations_AreAllEnumerated()
        {
            var result = new AlphaChainSettings
            {
                ShotClockSeconds = 0,
                EraInterval = 0,
                EraCount = 0,
                SniperBanSeconds = 0
            }.Validate();

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Violations.Length >= 4,
                $"Expected at least 4 violations but got {result.Violations.Length}.");
        }
    }
}
