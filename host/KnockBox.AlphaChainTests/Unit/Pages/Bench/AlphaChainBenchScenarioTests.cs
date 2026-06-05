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

            var outcome = await scenario.SubmitAsync("cat");

            Assert.IsTrue(outcome.TryGetSuccess(out var result));
            Assert.IsInstanceOfType<SubmitWordResult.AcceptedZeroPointTax>(result);
            Assert.AreEqual(0, scenario.Player(submitter)!.Score, "Taxed submitter scores 0.");
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
    }
}
