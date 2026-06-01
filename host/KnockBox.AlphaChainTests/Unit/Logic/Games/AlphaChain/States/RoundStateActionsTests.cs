using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards;
using KnockBox.AlphaChain.Services.Logic.Games.FSM;
using KnockBox.AlphaChain.Services.Logic.Scoring;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Tests.Unit.Support;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.AlphaChain.Tests.Unit.Logic.Games.AlphaChain.States
{
    /// <summary>
    /// Exercises action-card play through the real command path: Pivot/Amnesty queued
    /// effects on the next submission, and Time Thief's immediate vs. queued shot-clock theft.
    /// </summary>
    [TestClass]
    public class RoundStateActionsTests
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

        private async Task<(AlphaChainGameEngine Engine, AlphaChainGameState State)> StartGameAsync(
            StubWordListService words, int playerCount = 2, char? banned = null)
        {
            var engine = new AlphaChainGameEngine(
                words, new FixedRandomNumberService(), new ScoreCalculator(),
                _engineLoggerMock.Object, _stateLoggerMock.Object);

            var state = (AlphaChainGameState)(await engine.CreateStateAsync(_host)).Value!;
            for (int i = 0; i < playerCount; i++)
                state.RegisterPlayer(MakePlayer(i));

            await engine.StartAsync(_host, state);

            if (banned is { } b)
                state.Execute(() => state.BannedLetter = b);

            return (engine, state);
        }

        private static void GiveAction(AlphaChainGameState state, string playerId, string cardId) =>
            state.Execute(() => state.GamePlayers[playerId].ActionHand.Add(ActionLibrary.FindById(cardId)!));

        // ── Pivot ───────────────────────────────────────────────────────────

        [TestMethod]
        public async Task Pivot_ConsumesItself_AndClearsRequiredStartLetter()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), banned: 'z');
            using var _ = state;
            var current = state.TurnManager.CurrentPlayer!;

            // Force a required start letter the word does NOT match, then arm a Pivot.
            state.Execute(() => state.RequiredStartLetter = 'q');
            GiveAction(state, current, "pivot");

            var play = await engine.PlayActionAsync(current, "pivot", null, state);
            Assert.IsTrue(play.IsSuccess);
            Assert.AreEqual(ActionKind.Pivot, state.GamePlayers[current].PendingAction);

            // "cat" starts with 'c', not 'q' — only the Pivot lets this through.
            var outcome = await engine.SubmitWordAsync(current, "cat", state);
            Assert.IsTrue(outcome.TryGetSuccess(out var result));
            Assert.IsInstanceOfType<SubmitWordResult.Accepted>(result);

            // Pivot was spent by the submission it was queued for.
            Assert.IsNull(state.GamePlayers[current].PendingAction);
        }

        [TestMethod]
        public async Task Pivot_NotConsumed_WhenSubmissionRejected()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), banned: 'z');
            using var _ = state;
            var current = state.TurnManager.CurrentPlayer!;

            state.Execute(() => state.RequiredStartLetter = 'q');
            GiveAction(state, current, "pivot");
            await engine.PlayActionAsync(current, "pivot", null, state);

            // Not in the dictionary → rejected → the queued Pivot must survive.
            var outcome = await engine.SubmitWordAsync(current, "zzz", state);
            Assert.IsTrue(outcome.TryGetSuccess(out var result));
            Assert.IsInstanceOfType<SubmitWordResult.RejectedNotInDictionary>(result);
            Assert.AreEqual(ActionKind.Pivot, state.GamePlayers[current].PendingAction);
        }

        // ── Amnesty ─────────────────────────────────────────────────────────

        [TestMethod]
        public async Task Amnesty_SuppressesZeroPointTax_ExactlyOnce()
        {
            // Banned 'a' is inside "cat" → normally the Zero-Point Tax fires.
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), banned: 'a');
            using var _ = state;
            var current = state.TurnManager.CurrentPlayer!;

            GiveAction(state, current, "amnesty");
            await engine.PlayActionAsync(current, "amnesty", null, state);
            Assert.AreEqual(ActionKind.Amnesty, state.GamePlayers[current].PendingAction);

            var outcome = await engine.SubmitWordAsync(current, "cat", state);
            Assert.IsTrue(outcome.TryGetSuccess(out var result));

            // Tax suppressed: scored as a normal word (length 3, empty bay), not zeroed.
            Assert.IsInstanceOfType<SubmitWordResult.Accepted>(result);
            Assert.AreEqual(3, state.GamePlayers[current].Score);

            // Consumed — it only suppresses the tax once.
            Assert.IsNull(state.GamePlayers[current].PendingAction);
        }

        // ── Time Thief ──────────────────────────────────────────────────────

        [TestMethod]
        public async Task TimeThief_ShrinksPhaseEndTime_WhenTargetIsCurrentPlayer()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), banned: 'z');
            using var _ = state;
            var current = state.TurnManager.CurrentPlayer!;
            var opponent = state.TurnManager.TurnOrder[1];

            // The non-current opponent steals time from the active player.
            GiveAction(state, opponent, "time-thief");
            var before = state.PhaseEndTime;

            var play = await engine.PlayActionAsync(opponent, "time-thief", current, state);
            Assert.IsTrue(play.IsSuccess);

            Assert.AreEqual(before.AddSeconds(-5), state.PhaseEndTime);
            Assert.AreEqual(0, state.GamePlayers[current].QueuedTimePenaltySeconds);
        }

        [TestMethod]
        public async Task TimeThief_QueuesDebuff_WhenTargetIsNotCurrentPlayer()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), banned: 'z');
            using var _ = state;
            var current = state.TurnManager.CurrentPlayer!;
            var opponent = state.TurnManager.TurnOrder[1];

            // The active player targets the non-current opponent → the debit is queued.
            GiveAction(state, current, "time-thief");
            var play = await engine.PlayActionAsync(current, "time-thief", opponent, state);
            Assert.IsTrue(play.IsSuccess);
            Assert.AreEqual(5, state.GamePlayers[opponent].QueuedTimePenaltySeconds);

            // When the turn advances to the opponent, the queued time is shaved off their clock.
            await engine.SubmitWordAsync(current, "cat", state);
            Assert.AreEqual(opponent, state.TurnManager.CurrentPlayer);
            Assert.AreEqual(0, state.GamePlayers[opponent].QueuedTimePenaltySeconds);

            // Default shot clock is 12 s; with the 5 s debit the opponent has well under 12 s left.
            double remaining = (state.PhaseEndTime - DateTimeOffset.UtcNow).TotalSeconds;
            Assert.IsTrue(remaining < 9, $"Expected a shortened clock (<9s) but had {remaining:F1}s.");
        }

        [TestMethod]
        public async Task PlayAction_RejectedWhenCardNotHeld()
        {
            var (engine, state) = await StartGameAsync(new StubWordListService("cat"), banned: 'z');
            using var _ = state;
            var current = state.TurnManager.CurrentPlayer!;

            // Player holds no cards.
            var play = await engine.PlayActionAsync(current, "pivot", null, state);
            Assert.IsTrue(play.TryGetFailure(out var _ignored));
            Assert.IsNull(state.GamePlayers[current].PendingAction);
        }
    }
}
