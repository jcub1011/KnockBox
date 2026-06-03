using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards;
using KnockBox.AlphaChain.Services.Logic.Games.FSM;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;
using KnockBox.AlphaChain.Services.Logic.Games.Evaluation;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Tests.Unit.Support;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.AlphaChain.Tests.Unit.Logic.Games.AlphaChain.States
{
    /// <summary>
    /// Tax Collector is a reactive bounty card: when an opponent plays a banned-letter
    /// (Zero-Point Tax) word, every other active owner of a Tax Collector collects half of the
    /// points the word would have scored. The submitter always gets 0 and never collects from
    /// their own taxed word.
    /// </summary>
    [TestClass]
    public class RoundStateTaxCollectorTests
    {
        private Mock<ILogger<AlphaChainGameEngine>> _engineLoggerMock = default!;
        private Mock<ILogger<AlphaChainGameState>> _stateLoggerMock = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            _engineLoggerMock = new Mock<ILogger<AlphaChainGameEngine>>();
            _stateLoggerMock = new Mock<ILogger<AlphaChainGameState>>();
            _host = UserFactory.Create("Host", "host1");
        }

        private static User MakePlayer(int index) => UserFactory.Create($"Player{index}", $"p{index}-id");

        // Starts a game on a fixed dictionary with the banned letter set. "anchor" adds a flat
        // +6, so an "anchor"-only bay makes the taxed word's would-be score easy to reason about.
        private async Task<(AlphaChainGameEngine Engine, AlphaChainGameState State)> StartGameAsync(
            StubWordListService words, int playerCount, char banned)
        {
            var engine = new AlphaChainGameEngine(
                words, new FixedRandomNumberService(), new EngineEvaluator(), new ModifierCardFactory(),
                _engineLoggerMock.Object, _stateLoggerMock.Object);

            var state = (AlphaChainGameState)(await engine.CreateStateAsync(_host)).Value!;
            for (int i = 0; i < playerCount; i++)
                state.RegisterPlayer(MakePlayer(i));

            // Tutorials off so the game starts directly in RoundState.
            state.UpdateSettings(s => s with { EnableTutorials = false });
            await engine.StartAsync(_host, state);
            state.Execute(() => state.BannedLetter = banned);
            return (engine, state);
        }

        private static void GiveModifier(AlphaChainGameState state, string playerId, string cardId) =>
            state.Execute(() => state.GamePlayers[playerId].EngineBay.Add(TestModifierCards.Create(cardId)));

        private static void GiveTaxCollector(AlphaChainGameState state, string playerId) =>
            GiveModifier(state, playerId, TestModifierCards.TaxCollectorId);

        [TestMethod]
        public async Task OpponentTaxedWord_PaysHalfWouldBeScore_ToTaxCollectorOwner_SubmitterGetsZero()
        {
            // Banned 'a' is inside "cat". Submitter has an Anchor (+10) so the would-be score is
            // (length 3 + 10) = 13; the owner should collect round(13 × 0.5) = 7 (half-up).
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'a');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;
            var owner = state.TurnManager.TurnOrder[1];

            GiveModifier(state, submitter, "anchor");
            GiveTaxCollector(state, owner);

            var outcome = await engine.SubmitWordAsync(submitter, "cat", state);
            Assert.IsTrue(outcome.TryGetSuccess(out var result));
            Assert.IsInstanceOfType<SubmitWordResult.AcceptedZeroPointTax>(result);

            Assert.AreEqual(0, state.GamePlayers[submitter].Score, "Taxed submitter must score 0.");
            Assert.AreEqual(7, state.GamePlayers[owner].Score, "Owner collects half the would-be 13.");

            // The play feed records the bounty that was paid.
            Assert.AreEqual(7, state.PlayLog[^1].TaxBounty);

            // The score replay surfaces who stole the points (and how much) so the strip can list them.
            var replay = state.LatestScoreReplay!;
            Assert.AreEqual(7, replay.TaxBounty);
            CollectionAssert.AreEqual(
                new[] { state.GamePlayers[owner].DisplayName },
                replay.TaxCollectors!.ToArray());
            Assert.IsTrue(replay.HasSteal);
        }

        [TestMethod]
        public async Task OwnTaxedWord_PaysZero_AndNoSelfCollection()
        {
            // The Tax Collector owner is the one who plays the banned-letter word: they get 0 and
            // do not pay themselves.
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'a');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;

            GiveModifier(state, submitter, "anchor");
            GiveTaxCollector(state, submitter);

            var outcome = await engine.SubmitWordAsync(submitter, "cat", state);
            Assert.IsTrue(outcome.TryGetSuccess(out var result));
            Assert.IsInstanceOfType<SubmitWordResult.AcceptedZeroPointTax>(result);

            Assert.AreEqual(0, state.GamePlayers[submitter].Score);
            Assert.AreEqual(0, state.PlayLog[^1].TaxBounty);

            // No one collected, so the replay reports no steal.
            Assert.IsFalse(state.LatestScoreReplay!.HasSteal);
            Assert.AreEqual(0, state.LatestScoreReplay!.TaxBounty);
        }

        [TestMethod]
        public async Task CleanWord_PaysNoBounty()
        {
            // "cat" with banned 'z' is not taxed → no bounty regardless of owners.
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 2, banned: 'z');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;
            var owner = state.TurnManager.TurnOrder[1];

            GiveModifier(state, submitter, "anchor");
            GiveTaxCollector(state, owner);

            var outcome = await engine.SubmitWordAsync(submitter, "cat", state);
            Assert.IsTrue(outcome.TryGetSuccess(out var result));
            Assert.IsInstanceOfType<SubmitWordResult.Accepted>(result);

            Assert.AreEqual(0, state.GamePlayers[owner].Score, "No tax → no bounty.");
            Assert.AreEqual(0, state.PlayLog[^1].TaxBounty);
        }

        [TestMethod]
        public async Task MultipleOwners_EachCollectTheBounty()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), playerCount: 3, banned: 'a');
            using var _ = state;
            var submitter = state.TurnManager.CurrentPlayer!;
            var owner1 = state.TurnManager.TurnOrder[1];
            var owner2 = state.TurnManager.TurnOrder[2];

            GiveModifier(state, submitter, "anchor"); // would-be 13
            GiveTaxCollector(state, owner1);
            GiveTaxCollector(state, owner2);

            await engine.SubmitWordAsync(submitter, "cat", state);

            Assert.AreEqual(7, state.GamePlayers[owner1].Score);
            Assert.AreEqual(7, state.GamePlayers[owner2].Score);

            // Both owners are listed (sorted) on the replay so the strip shows every thief.
            var replay = state.LatestScoreReplay!;
            Assert.AreEqual(7, replay.TaxBounty);
            CollectionAssert.AreEquivalent(
                new[] { state.GamePlayers[owner1].DisplayName, state.GamePlayers[owner2].DisplayName },
                replay.TaxCollectors!.ToArray());
        }
    }
}
