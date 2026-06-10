using KnockBox.AlphaChain.Pages.Bench;
using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;
using KnockBox.AlphaChain.Services.Logic.Games.Evaluation;
using KnockBox.AlphaChain.Services.Logic.Games.FSM;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Tests.Unit.Support;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.AlphaChain.Tests.Unit.Pages.Bench
{
    /// <summary>
    /// Guards that the hidden card bench's scenario harness drives the <b>real</b> engine, so the
    /// player-to-player cards it exists to test behave exactly as they do in a live match. Mirrors
    /// <c>RoundStateTaxCollectorTests</c> but reaches the engine through the bench's public surface
    /// (Reset → SetBannedLetter → SetBay → Submit), proving the bench path and the engine agree.
    /// </summary>
    [TestClass]
    public class AlphaChainBenchScenarioTests
    {
        private static AlphaChainBenchScenario CreateScenario() => new(
            new FixedRandomNumberService(),
            new EngineEvaluator(),
            new ModifierCardFactory(),
            Mock.Of<ILogger<AlphaChainGameEngine>>(),
            Mock.Of<ILogger<AlphaChainGameState>>());

        [TestMethod]
        public async Task TaxedOpponentWord_PaysHalfToTaxCollectorOwner_ThroughTheBench()
        {
            using var scenario = CreateScenario();
            await scenario.ResetAsync(playerCount: 3);

            var submitter = scenario.CurrentPlayerId!.Value;   // P0 holds the turn after reset
            var owner = scenario.TurnOrder[1];                 // P1 owns the Tax Collector

            scenario.SetBannedLetter('a');                     // 'a' is inside "cat" → Zero-Point Tax
            scenario.SetBay(submitter, [ModifierId.TheAnchor]);// would-be score = length 3 + 10 = 13
            scenario.SetBay(owner, [ModifierId.TaxCollector]);
            // The era ban never taxes the last-place player; park the submitter above the field so the
            // taxed-word path runs. The taxed word still scores 0, so they stay at this baseline.
            scenario.SetScore(submitter, 100);

            var outcome = await scenario.SubmitAsync("cat");

            Assert.IsTrue(outcome.TryGetSuccess(out var result));
            Assert.IsInstanceOfType<SubmitWordResult.AcceptedZeroPointTax>(result);
            Assert.AreEqual(100, scenario.Player(submitter)!.Score, "Taxed word adds 0 → submitter unchanged.");
            Assert.AreEqual(7, scenario.Player(owner)!.Score, "Owner collects half the would-be 13.");

            var replay = scenario.LatestReplay!;
            Assert.IsTrue(replay.HasSteal);
            Assert.AreEqual(7, replay.TaxBounty);
            CollectionAssert.Contains(replay.TaxCollectors!.ToArray(), scenario.Player(owner)!.DisplayName);
        }

        [TestMethod]
        public async Task CleanWord_PaysNoBounty_ThroughTheBench()
        {
            using var scenario = CreateScenario();
            await scenario.ResetAsync(playerCount: 2);

            var submitter = scenario.CurrentPlayerId!.Value;
            var owner = scenario.TurnOrder[1];

            scenario.SetBannedLetter('z');                     // 'z' is not in "cat" → no tax, no bounty
            scenario.SetBay(owner, [ModifierId.TaxCollector]);

            var outcome = await scenario.SubmitAsync("cat");

            Assert.IsTrue(outcome.TryGetSuccess(out var result));
            Assert.IsInstanceOfType<SubmitWordResult.Accepted>(result);
            Assert.AreEqual(0, scenario.Player(owner)!.Score, "No tax → no bounty.");
            Assert.IsFalse(scenario.LatestReplay!.HasSteal);
        }

        [TestMethod]
        public async Task SkipTurn_HandsTheSeatToTheNextPlayer()
        {
            using var scenario = CreateScenario();
            await scenario.ResetAsync(playerCount: 3);

            var first = scenario.CurrentPlayerId!.Value;
            Assert.AreEqual(scenario.TurnOrder[0], first);

            var skip = await scenario.SkipTurnAsync();
            Assert.IsFalse(skip.IsFailure);
            Assert.AreEqual(scenario.TurnOrder[1], scenario.CurrentPlayerId!.Value);
        }

        [TestMethod]
        public async Task RemainingSeconds_DrivesChronoSyphonPayout_ThroughTheBench()
        {
            using var scenario = CreateScenario();
            await scenario.ResetAsync(playerCount: 2);

            var submitter = scenario.CurrentPlayerId!.Value; // P0 holds the turn after reset
            var owner = scenario.TurnOrder[1];                // P1 owns the Chrono Syphon

            scenario.SetBannedLetter('z');                    // 'z' is not in "cat" → a clean accepted word
            scenario.SetBay(owner, [ModifierId.ChronoSyphon]);

            // The submitter is positioned with exactly 5 whole seconds left on their clock.
            var outcome = await scenario.SubmitAsync("cat", remainingSeconds: 5);

            Assert.IsTrue(outcome.TryGetSuccess(out var result));
            Assert.IsInstanceOfType<SubmitWordResult.Accepted>(result);
            Assert.AreEqual(5, scenario.Player(owner)!.Score,
                "Chrono Syphon banks +1 per whole second left on the opponent's clock.");
        }

        [TestMethod]
        public async Task EngineNotices_AreReachableAndEmptyAfterACleanSubmit()
        {
            using var scenario = CreateScenario();
            await scenario.ResetAsync(playerCount: 2);

            scenario.SetBannedLetter('z');
            var outcome = await scenario.SubmitAsync("cat");

            Assert.IsTrue(outcome.TryGetSuccess(out _));
            Assert.IsNotNull(scenario.EngineNotices);
            Assert.AreEqual(0, scenario.EngineNotices.Count, "No reflective card fired → no off-submission notices.");
        }

        [TestMethod]
        public async Task SetScore_ClampsNegativeInputToZero()
        {
            using var scenario = CreateScenario();
            await scenario.ResetAsync(playerCount: 2);

            var player = scenario.CurrentPlayerId!.Value;
            scenario.SetScore(player, -50);

            Assert.AreEqual(0, scenario.Player(player)!.Score, "Negative scores clamp to 0.");
        }

        [TestMethod]
        public async Task SetBannedLetter_NormalizesCaseAndRejectsNonLetters()
        {
            using var scenario = CreateScenario();
            await scenario.ResetAsync(playerCount: 2);

            scenario.SetBannedLetter('A');
            Assert.AreEqual('a', scenario.BannedLetter, "An upper-case letter is lower-cased.");

            scenario.SetBannedLetter('5');
            Assert.IsNull(scenario.BannedLetter, "A non-letter clears the ban rather than banning a digit.");

            // With no ban in effect, a word that would otherwise be taxed scores cleanly.
            var outcome = await scenario.SubmitAsync("cat");
            Assert.IsTrue(outcome.TryGetSuccess(out var result));
            Assert.IsInstanceOfType<SubmitWordResult.Accepted>(result);
        }

        [TestMethod]
        public async Task SubmitAndSkip_FailGracefully_BeforeReset()
        {
            using var scenario = CreateScenario(); // never started — no active scenario

            var submit = await scenario.SubmitAsync("cat");
            Assert.IsTrue(submit.TryGetFailure(out var submitError));
            Assert.AreEqual("No active scenario.", submitError.PublicMessage);

            var skip = await scenario.SkipTurnAsync();
            Assert.IsTrue(skip.TryGetFailure(out var skipError));
            Assert.AreEqual("No active scenario.", skipError.PublicMessage);
        }
    }
}
