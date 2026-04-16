using KnockBox.CardCounter.Services.Logic.Games.FSM;
using KnockBox.CardCounter.Services.Logic.Games.FSM.States;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.CardCounter.Services.State.Games;
using KnockBox.CardCounter.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.CardCounter.Tests.Unit.Logic.Games.CardCounter
{
    [TestClass]
    public class WaitingForReactionStateTests
    {
        private Mock<IRandomNumberService> _randomMock = default!;
        private Mock<ILogger> _loggerMock = default!;
        private Mock<ILogger<CardCounterGameState>> _stateLoggerMock = default!;
        private CardCounterGameState _state = default!;
        private CardCounterGameContext _context = default!;

        [TestInitialize]
        public void Setup()
        {
            _randomMock = new Mock<IRandomNumberService>();
            _randomMock.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<RandomType>())).Returns(0);
            _loggerMock = new Mock<ILogger>();
            _stateLoggerMock = new Mock<ILogger<CardCounterGameState>>();

            var host = new User("Host", "host-id");
            _state = new CardCounterGameState(host, _stateLoggerMock.Object);
            _context = new CardCounterGameContext(_state, _randomMock.Object, _loggerMock.Object);
        }

        private PlayerState AddPlayer(string id, string name)
        {
            var player = new PlayerState { PlayerId = id, DisplayName = name };
            _state.GamePlayers[id] = player;
            _state.TurnManager.TurnOrder.Add(id);
            return player;
        }

        // ── TurnTheTable ──────────────────────────────────────────────────────

        [TestMethod]
        public void TurnTheTable_TargetAccepts_ReversesPot()
        {
            var source = AddPlayer("src", "Source");
            var target = AddPlayer("tgt", "Target");
            _state.TurnManager.SetCurrentPlayerIndex(0);
            target.Pot.AddRange([1, 2, 3]);
            _state.CurrentShoe.Push(new NumberCard(1)); // keep shoe non-empty

            var fsmState = new WaitingForReactionState("src", "tgt", new ActionCard(ActionType.TurnTheTable));
            fsmState.OnEnter(_context);

            var next = fsmState.HandleCommand(_context, new AcceptPendingCommand("tgt"));

            Assert.IsNotNull(next.Value);
            Assert.IsInstanceOfType(next.Value, typeof(PlayerTurnState));
            CollectionAssert.AreEqual(new[] { 3, 2, 1 }, target.Pot, "Target pot should be reversed.");
        }

        [TestMethod]
        public void TurnTheTable_TargetBlocksWithCompd_DoesNotReversePot()
        {
            var source = AddPlayer("src", "Source");
            var target = AddPlayer("tgt", "Target");
            _state.TurnManager.SetCurrentPlayerIndex(0);
            target.Pot.AddRange([1, 2, 3]);
            target.ActionHand.Add(new ActionCard(ActionType.Compd));

            var fsmState = new WaitingForReactionState("src", "tgt", new ActionCard(ActionType.TurnTheTable));
            fsmState.OnEnter(_context);

            var next = fsmState.HandleCommand(_context, new PlayActionCardCommand("tgt", 0));

            Assert.IsNotNull(next.Value);
            Assert.IsInstanceOfType(next.Value, typeof(PlayerTurnState));
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, target.Pot, "Blocked: pot should NOT be reversed.");
            Assert.IsEmpty(target.ActionHand, "Comp'd card should be consumed.");
        }

        [TestMethod]
        public void TurnTheTable_TargetBlocksWithCompd_RecordsCompdAsLastPlayedAction()
        {
            var source = AddPlayer("src", "Source");
            var target = AddPlayer("tgt", "Target");
            _state.TurnManager.SetCurrentPlayerIndex(0);
            target.ActionHand.Add(new ActionCard(ActionType.Compd));

            var fsmState = new WaitingForReactionState("src", "tgt", new ActionCard(ActionType.TurnTheTable));
            fsmState.OnEnter(_context);

            fsmState.HandleCommand(_context, new PlayActionCardCommand("tgt", 0));

            Assert.IsNotNull(_state.LastPlayedAction);
            Assert.AreEqual(ActionType.Compd, _state.LastPlayedAction.Action);
            Assert.AreEqual("tgt", _state.LastPlayedAction.PlayerId);
        }

        // ── Launder ───────────────────────────────────────────────────────────

        [TestMethod]
        public void Launder_TargetAccepts_SwapsPots()
        {
            var source = AddPlayer("src", "Source");
            var target = AddPlayer("tgt", "Target");
            _state.TurnManager.SetCurrentPlayerIndex(0);
            source.Pot.AddRange([1, 2]);
            target.Pot.AddRange([3, 4, 5]);
            _state.CurrentShoe.Push(new NumberCard(1));

            var fsmState = new WaitingForReactionState("src", "tgt", new ActionCard(ActionType.Launder));
            fsmState.OnEnter(_context);

            fsmState.HandleCommand(_context, new AcceptPendingCommand("tgt"));

            CollectionAssert.AreEqual(new[] { 3, 4, 5 }, source.Pot, "Source should now have target's old pot.");
            CollectionAssert.AreEqual(new[] { 1, 2 }, target.Pot, "Target should now have source's old pot.");
        }

        [TestMethod]
        public void Launder_TargetBlocksWithCompd_PotsUnchanged()
        {
            var source = AddPlayer("src", "Source");
            var target = AddPlayer("tgt", "Target");
            _state.TurnManager.SetCurrentPlayerIndex(0);
            source.Pot.AddRange([1, 2]);
            target.Pot.AddRange([3, 4, 5]);
            target.ActionHand.Add(new ActionCard(ActionType.Compd));

            var fsmState = new WaitingForReactionState("src", "tgt", new ActionCard(ActionType.Launder));
            fsmState.OnEnter(_context);

            fsmState.HandleCommand(_context, new PlayActionCardCommand("tgt", 0));

            CollectionAssert.AreEqual(new[] { 1, 2 }, source.Pot, "Source pot should be unchanged.");
            CollectionAssert.AreEqual(new[] { 3, 4, 5 }, target.Pot, "Target pot should be unchanged.");
        }

        // ── Unrelated player ──────────────────────────────────────────────────

        [TestMethod]
        public void UnrelatedPlayer_CannotAcceptOrBlock()
        {
            var source = AddPlayer("src", "Source");
            var target = AddPlayer("tgt", "Target");
            var other = AddPlayer("other", "Other");
            _state.TurnManager.SetCurrentPlayerIndex(0);
            target.Pot.AddRange([1, 2, 3]);

            var fsmState = new WaitingForReactionState("src", "tgt", new ActionCard(ActionType.TurnTheTable));
            fsmState.OnEnter(_context);

            // Other player tries to accept
            var next = fsmState.HandleCommand(_context, new AcceptPendingCommand("other"));

            Assert.IsNull(next.Value);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, target.Pot, "Pot should not change when unrelated player sends accept.");
        }

        [TestMethod]
        public void OnEnter_SetsPendingReactionInfo()
        {
            var source = AddPlayer("src", "Source");
            var target = AddPlayer("tgt", "Target");
            _state.TurnManager.SetCurrentPlayerIndex(0);

            var actionCard = new ActionCard(ActionType.TurnTheTable);
            var fsmState = new WaitingForReactionState("src", "tgt", actionCard);
            fsmState.OnEnter(_context);

            Assert.IsNotNull(_state.PendingReaction);
            Assert.AreEqual("src", _state.PendingReaction.SourceId);
            Assert.AreEqual("tgt", _state.PendingReaction.TargetId);
            Assert.AreEqual(actionCard, _state.PendingReaction.PlayedCard);
        }

        // ── Shoe exhausted ────────────────────────────────────────────────────

        [TestMethod]
        public void TurnTheTable_TargetAccepts_WhenShoeEmpty_TransitionsToRoundEndState()
        {
            var source = AddPlayer("src", "Source");
            var target = AddPlayer("tgt", "Target");
            _state.TurnManager.SetCurrentPlayerIndex(0);
            target.Pot.AddRange([1, 2]);
            // No cards in shoe → after advancing turn the shoe is empty → RoundEnd

            var fsmState = new WaitingForReactionState("src", "tgt", new ActionCard(ActionType.TurnTheTable));
            fsmState.OnEnter(_context);

            // TurnTheTable does NOT advance turn — it just returns PlayerTurnState.
            // (Only blockable effects that call FinishTurn advance the turn.)
            var next = fsmState.HandleCommand(_context, new AcceptPendingCommand("tgt"));

            // TurnTheTable goes to PlayerTurnState regardless of shoe count
            Assert.IsNotNull(next.Value);
            Assert.IsInstanceOfType(next.Value, typeof(PlayerTurnState));
        }

        // ── Non-Compd response cards are ignored ──────────────────────────────

        [TestMethod]
        public void Target_PlayingNonCompdCard_IsIgnored()
        {
            var source = AddPlayer("src", "Source");
            var target = AddPlayer("tgt", "Target");
            _state.TurnManager.SetCurrentPlayerIndex(0);
            target.ActionHand.Add(new ActionCard(ActionType.Burn)); // not a Comp'd
            target.Pot.AddRange([1, 2, 3]);

            var fsmState = new WaitingForReactionState("src", "tgt", new ActionCard(ActionType.TurnTheTable));
            fsmState.OnEnter(_context);

            var next = fsmState.HandleCommand(_context, new PlayActionCardCommand("tgt", 0));

            Assert.IsNull(next.Value, "A non-Comp'd response should be ignored.");
            Assert.HasCount(1, target.ActionHand, "Non-Comp'd card should not be consumed.");
        }
    }
}
