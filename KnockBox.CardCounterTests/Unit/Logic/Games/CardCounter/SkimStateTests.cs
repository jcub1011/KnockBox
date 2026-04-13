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
    public class SkimStateTests
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

        [TestMethod]
        public void Skim_SourceSelectsThenTargetAccepts_SwapsSelectedDigits()
        {
            var source = AddPlayer("src", "Source");
            var target = AddPlayer("tgt", "Target");
            _state.TurnManager.SetCurrentPlayerIndex(0);

            // source.Pot = [1, 2, 3], target.Pot = [4, 5, 6]
            source.Pot.AddRange([1, 2, 3]);
            target.Pot.AddRange([4, 5, 6]);

            var fsmState = new SkimState("src", "tgt", new ActionCard(ActionType.Skim));
            fsmState.OnEnter(_context);

            // Source selects index 1 ↔ target index 2 (2 ↔ 6)
            var afterSelect = fsmState.HandleCommand(_context, new SkimSelectCommand("src", 1, 2));
            Assert.IsNull(afterSelect.Value, "Should not resolve yet; target hasn't accepted.");

            // Target accepts
            var next = fsmState.HandleCommand(_context, new AcceptPendingCommand("tgt"));

            Assert.IsNotNull(next.Value);
            Assert.IsInstanceOfType(next.Value, typeof(PlayerTurnState));
            Assert.AreEqual(6, source.Pot[1], "Source digit[1] should be 6 (swapped from target[2]).");
            Assert.AreEqual(2, target.Pot[2], "Target digit[2] should be 2 (swapped from source[1]).");
        }

        [TestMethod]
        public void Skim_TargetAcceptsThenSourceSelects_SwapsSelectedDigits()
        {
            var source = AddPlayer("src", "Source");
            var target = AddPlayer("tgt", "Target");
            _state.TurnManager.SetCurrentPlayerIndex(0);

            source.Pot.AddRange([7, 8]);
            target.Pot.AddRange([9, 10]);

            var fsmState = new SkimState("src", "tgt", new ActionCard(ActionType.Skim));
            fsmState.OnEnter(_context);

            // Target accepts first
            var afterAccept = fsmState.HandleCommand(_context, new AcceptPendingCommand("tgt"));
            Assert.IsNull(afterAccept.Value, "Should not resolve yet; source hasn't selected.");

            // Source selects index 0 ↔ target index 1 (7 ↔ 10)
            var next = fsmState.HandleCommand(_context, new SkimSelectCommand("src", 0, 1));

            Assert.IsNotNull(next.Value);
            Assert.IsInstanceOfType(next.Value, typeof(PlayerTurnState));
            Assert.AreEqual(10, source.Pot[0], "Source digit[0] should be 10 (swapped from target[1]).");
            Assert.AreEqual(7, target.Pot[1], "Target digit[1] should be 7 (swapped from source[0]).");
        }

        [TestMethod]
        public void Skim_TargetBlocksWithCompd_NoSwapOccurs()
        {
            var source = AddPlayer("src", "Source");
            var target = AddPlayer("tgt", "Target");
            _state.TurnManager.SetCurrentPlayerIndex(0);
            source.Pot.AddRange([1, 2]);
            target.Pot.AddRange([3, 4]);
            target.ActionHand.Add(new ActionCard(ActionType.Compd));

            var fsmState = new SkimState("src", "tgt", new ActionCard(ActionType.Skim));
            fsmState.OnEnter(_context);

            var next = fsmState.HandleCommand(_context, new PlayActionCardCommand("tgt", 0));

            Assert.IsNotNull(next.Value);
            Assert.IsInstanceOfType(next.Value, typeof(PlayerTurnState));
            CollectionAssert.AreEqual(new[] { 1, 2 }, source.Pot, "Source pot should be unchanged after Comp'd block.");
            CollectionAssert.AreEqual(new[] { 3, 4 }, target.Pot, "Target pot should be unchanged after Comp'd block.");
            Assert.AreEqual(0, target.ActionHand.Count, "Comp'd card should be consumed.");
        }

        [TestMethod]
        public void Skim_TargetBlocksWithCompd_RecordsCompdAsLastPlayedAction()
        {
            var source = AddPlayer("src", "Source");
            var target = AddPlayer("tgt", "Target");
            _state.TurnManager.SetCurrentPlayerIndex(0);
            source.Pot.Add(1);
            target.Pot.Add(2);
            target.ActionHand.Add(new ActionCard(ActionType.Compd));

            var fsmState = new SkimState("src", "tgt", new ActionCard(ActionType.Skim));
            fsmState.OnEnter(_context);

            fsmState.HandleCommand(_context, new PlayActionCardCommand("tgt", 0));

            Assert.IsNotNull(_state.LastPlayedAction);
            Assert.AreEqual(ActionType.Compd, _state.LastPlayedAction.Action);
        }

        [TestMethod]
        public void Skim_OnEnter_SetsPendingReactionInfo()
        {
            AddPlayer("src", "Source");
            AddPlayer("tgt", "Target");
            _state.TurnManager.SetCurrentPlayerIndex(0);

            var actionCard = new ActionCard(ActionType.Skim);
            var fsmState = new SkimState("src", "tgt", actionCard);
            fsmState.OnEnter(_context);

            Assert.IsNotNull(_state.PendingReaction);
            Assert.AreEqual("src", _state.PendingReaction.SourceId);
            Assert.AreEqual("tgt", _state.PendingReaction.TargetId);
        }

        [TestMethod]
        public void Skim_OutOfRangeSourceIndex_DefaultsToLastDigit()
        {
            var source = AddPlayer("src", "Source");
            var target = AddPlayer("tgt", "Target");
            _state.TurnManager.SetCurrentPlayerIndex(0);
            source.Pot.AddRange([1, 2, 3]);
            target.Pot.AddRange([4, 5, 6]);

            var fsmState = new SkimState("src", "tgt", new ActionCard(ActionType.Skim));
            fsmState.OnEnter(_context);

            // Source selects (but with an out-of-range source index — should default to last)
            fsmState.HandleCommand(_context, new SkimSelectCommand("src", 99, 0));
            var next = fsmState.HandleCommand(_context, new AcceptPendingCommand("tgt"));

            Assert.IsNotNull(next.Value);
            // The out-of-range index defaults to last digit (2→index 2, value=3) swapping with target[0] (value=4)
            Assert.AreEqual(4, source.Pot[2], "Out-of-range source index should default to last digit.");
            Assert.AreEqual(3, target.Pot[0], "Out-of-range index swap should use target[0].");
        }

        [TestMethod]
        public void Skim_TargetSelectsBeforeSource_UpdatesPendingReactionDigitIndices()
        {
            var source = AddPlayer("src", "Source");
            var target = AddPlayer("tgt", "Target");
            _state.TurnManager.SetCurrentPlayerIndex(0);
            source.Pot.AddRange([1, 2]);
            target.Pot.AddRange([3, 4]);

            var fsmState = new SkimState("src", "tgt", new ActionCard(ActionType.Skim));
            fsmState.OnEnter(_context);

            fsmState.HandleCommand(_context, new SkimSelectCommand("src", 0, 1));

            // PendingReaction should now reflect the selected digit indices
            Assert.IsNotNull(_state.PendingReaction?.SourceDigitIndex);
            Assert.AreEqual(0, _state.PendingReaction!.SourceDigitIndex);
            Assert.AreEqual(1, _state.PendingReaction.TargetDigitIndex);
        }
    }
}
