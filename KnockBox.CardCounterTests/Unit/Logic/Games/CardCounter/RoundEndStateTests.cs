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
    public class RoundEndStateTests
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

        /// <summary>
        /// Pushes cards onto the main deck to simulate a non-empty deck.
        /// The shoe deal will pop cards from the main deck.
        /// </summary>
        private void PushCardsToMainDeck(int count)
        {
            for (int i = 0; i < count; i++)
                _state.MainDeck.Push(new NumberCard(i % 10));
        }

        [TestMethod]
        public void OnEnter_WhenDeckHasCards_SetsIsNewShoe()
        {
            AddPlayer("p1", "Player 1");
            // Push enough cards for the minimum shoe size
            PushCardsToMainDeck(_state.Config.MinShoeSize);

            var fsmState = new RoundEndState();
            fsmState.OnEnter(_context);

            Assert.IsTrue(_state.IsNewShoe, "IsNewShoe should be true after dealing a new shoe.");
        }

        [TestMethod]
        public void OnEnter_WhenDeckEmpty_TransitionsToGameOver()
        {
            AddPlayer("p1", "Player 1");
            // Main deck is empty

            var fsmState = new RoundEndState();
            var next = fsmState.OnEnter(_context);

            Assert.IsInstanceOfType(next.Value, typeof(GameOverState), "Empty deck should trigger a transition to GameOverState.");
        }

        [TestMethod]
        public void OnEnter_WhenDeckHasCards_PopulatesCurrentShoe()
        {
            AddPlayer("p1", "Player 1");
            PushCardsToMainDeck(_state.Config.MinShoeSize + 5);

            var fsmState = new RoundEndState();
            fsmState.OnEnter(_context);

            Assert.IsNotEmpty(_state.CurrentShoe, "Current shoe should be populated from the main deck.");
        }

        [TestMethod]
        public void OnEnter_WhenDeckHasCards_DealActionCardsToPlayers()
        {
            var p1 = AddPlayer("p1", "Player 1");
            PushCardsToMainDeck(_state.Config.MinShoeSize);

            var fsmState = new RoundEndState();
            fsmState.OnEnter(_context);

            Assert.HasCount(_state.Config.ActionsDealtPerRound, p1.ActionHand,
                "Each player should receive action cards on round end.");
        }

        [TestMethod]
        public void OnEnter_AllPlayersUnderHandLimit_ReturnsPlayerTurnState()
        {
            AddPlayer("p1", "Player 1");
            PushCardsToMainDeck(_state.Config.MinShoeSize);

            var fsmState = new RoundEndState();
            var next = fsmState.OnEnter(_context);

            // When no player exceeds the limit, OnEnter should return PlayerTurnState immediately
            Assert.IsInstanceOfType(next.Value, typeof(PlayerTurnState));
        }

        [TestMethod]
        public void HandleCommand_Discard_ValidIndices_RemovesCards()
        {
            var p1 = AddPlayer("p1", "Player 1");
            PushCardsToMainDeck(_state.Config.MinShoeSize);

            // Pre-fill the hand to beyond the limit
            int limit = _state.Config.ActionHandLimit;
            for (int i = 0; i <= limit; i++) // limit+1 cards
                p1.ActionHand.Add(new ActionCard(ActionType.Burn));

            // Manually set state since OnEnter auto-transitions when nobody is over limit

            var fsmState = new RoundEndState();

            // We need the shoe to be pre-populated for context purposes
            _context.DealNextShoe();
            _context.DealActionCards(); // this would add ActionsDealtPerRound more cards (already over limit)

            // Discard the extra cards to bring under limit
            int currentCount = p1.ActionHand.Count;
            int excessCount = currentCount - limit;
            int[] indicesToDiscard = [.. Enumerable.Range(limit, excessCount)];

            var next = fsmState.HandleCommand(_context, new DiscardActionCardsCommand("p1", indicesToDiscard));

            Assert.HasCount(limit, p1.ActionHand, "Player should be at the action hand limit after discarding.");
        }

        [TestMethod]
        public void HandleCommand_Discard_InvalidIndices_IsNoOp()
        {
            var p1 = AddPlayer("p1", "Player 1");
            int limit = _state.Config.ActionHandLimit;
            for (int i = 0; i <= limit; i++)
                p1.ActionHand.Add(new ActionCard(ActionType.Burn));

            var fsmState = new RoundEndState();

            int countBefore = p1.ActionHand.Count;
            // Pass an out-of-range index
            fsmState.HandleCommand(_context, new DiscardActionCardsCommand("p1", [999]));

            Assert.HasCount(countBefore, p1.ActionHand, "Invalid indices should leave hand unchanged.");
        }

        [TestMethod]
        public void HandleCommand_Discard_DuplicateIndices_IsNoOp()
        {
            var p1 = AddPlayer("p1", "Player 1");
            int limit = _state.Config.ActionHandLimit;
            for (int i = 0; i <= limit; i++)
                p1.ActionHand.Add(new ActionCard(ActionType.Burn));

            var fsmState = new RoundEndState();

            int countBefore = p1.ActionHand.Count;
            // Duplicate index (same card twice)
            fsmState.HandleCommand(_context, new DiscardActionCardsCommand("p1", [0, 0]));

            Assert.HasCount(countBefore, p1.ActionHand, "Duplicate indices should be rejected.");
        }

        [TestMethod]
        public void HandleCommand_Discard_NotEnoughCardsDiscarded_IsNoOp()
        {
            var p1 = AddPlayer("p1", "Player 1");
            int limit = _state.Config.ActionHandLimit;
            // Two cards over the limit
            for (int i = 0; i < limit + 2; i++)
                p1.ActionHand.Add(new ActionCard(ActionType.Burn));

            var fsmState = new RoundEndState();

            int countBefore = p1.ActionHand.Count;
            // Only discard one — still over limit, so command should be rejected
            fsmState.HandleCommand(_context, new DiscardActionCardsCommand("p1", [0]));

            Assert.HasCount(countBefore, p1.ActionHand,
                "Discarding too few cards (still over limit) should be rejected.");
        }

        [TestMethod]
        public void HandleCommand_Discard_AllPlayersUnderLimit_TransitionsToPlayerTurnState()
        {
            var p1 = AddPlayer("p1", "Player 1");
            var p2 = AddPlayer("p2", "Player 2");
            int limit = _state.Config.ActionHandLimit;

            // Both players have one card over limit
            for (int i = 0; i <= limit; i++)
                p1.ActionHand.Add(new ActionCard(ActionType.Burn));
            for (int i = 0; i <= limit; i++)
                p2.ActionHand.Add(new ActionCard(ActionType.Burn));

            // p2 already at limit
            while (p2.ActionHand.Count > limit) p2.ActionHand.RemoveAt(p2.ActionHand.Count - 1);

            var fsmState = new RoundEndState();
            var next = fsmState.HandleCommand(_context, new DiscardActionCardsCommand("p1", [limit]));

            Assert.IsNotNull(next.Value);
            Assert.IsInstanceOfType(next.Value, typeof(PlayerTurnState), "When all players are under limit, transition to PlayerTurnState.");
        }

        [TestMethod]
        public void HandleCommand_NonDiscardCommand_IsIgnored()
        {
            AddPlayer("p1", "Player 1");
            var fsmState = new RoundEndState();

            var next = fsmState.HandleCommand(_context, new DrawCardCommand("p1"));

            Assert.IsNull(next.Value, "Non-discard commands should be ignored in RoundEndState.");
        }
    }
}
