using KnockBox.Services.Logic.Games.CardCounter.FSM;
using KnockBox.Services.Logic.Games.CardCounter.FSM.States;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.CardCounter;
using KnockBox.Services.State.Games.CardCounter.Data;
using KnockBox.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBoxTests.Unit.Logic.Games.CardCounter
{
    [TestClass]
    public class CardCounterGameContextBehaviorTests
    {
        private Mock<IRandomNumberService> _randomMock = default!;
        private Mock<ILogger> _loggerMock = default!;
        private Mock<ILogger<CardCounterGameState>> _stateLoggerMock = default!;

        [TestInitialize]
        public void Setup()
        {
            _randomMock = new Mock<IRandomNumberService>();
            _loggerMock = new Mock<ILogger>();
            _stateLoggerMock = new Mock<ILogger<CardCounterGameState>>();
        }

        [TestMethod]
        public void ApplyOperatorCard_EmptyPot_IsNoOp()
        {
            var host = new User("Host", "host-id");
            using var state = new CardCounterGameState(host, _stateLoggerMock.Object);
            var context = new CardCounterGameContext(state, _randomMock.Object, _loggerMock.Object);
            foreach (var op in Enum.GetValues<Operator>())
            {
                var player = new PlayerState
                {
                    PlayerId = "p1",
                    DisplayName = "Player 1",
                    Balance = 42
                };

                context.ApplyOperatorCard(player, new OperatorCard(op));

                Assert.AreEqual(42L, player.Balance);
                Assert.AreEqual(0, player.Pot.Count);
            }
        }

        [TestMethod]
        public void WaitingForReaction_CompdResponse_UpdatesLastPlayedActionToCompd()
        {
            var host = new User("Host", "host-id");
            var sourceUser = new User("Source", "source-id");
            var targetUser = new User("Target", "target-id");
            using var state = new CardCounterGameState(host, _stateLoggerMock.Object);
            var context = new CardCounterGameContext(state, _randomMock.Object, _loggerMock.Object);
            var source = new PlayerState { PlayerId = sourceUser.Id, DisplayName = sourceUser.Name };
            var target = new PlayerState { PlayerId = targetUser.Id, DisplayName = targetUser.Name };
            target.ActionHand.Add(new ActionCard(ActionType.Compd));
            state.GamePlayers[source.PlayerId] = source;
            state.GamePlayers[target.PlayerId] = target;

            var fsmState = new WaitingForReactionState(
                source.PlayerId,
                target.PlayerId,
                new ActionCard(ActionType.TurnTheTable));
            fsmState.OnEnter(context);

            var next = fsmState.HandleCommand(context, new PlayActionCardCommand(target.PlayerId, 0));

            Assert.IsNotNull(next);
            Assert.IsInstanceOfType(next, typeof(PlayerTurnState));
            Assert.IsNotNull(state.LastPlayedAction);
            Assert.AreEqual(target.PlayerId, state.LastPlayedAction.PlayerId);
            Assert.AreEqual(ActionType.Compd, state.LastPlayedAction.Action);
            Assert.AreEqual(1, state.DiscardHistory.Count);
            Assert.IsTrue(state.DiscardHistory[0].IsActionCard);
            Assert.AreEqual("Comp'd", state.DiscardHistory[0].Description);
        }
    }
}
