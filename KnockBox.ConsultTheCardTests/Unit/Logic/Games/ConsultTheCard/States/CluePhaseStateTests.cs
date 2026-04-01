using KnockBox.Services.Logic.Games.ConsultTheCard.FSM;
using KnockBox.Services.Logic.Games.ConsultTheCard.FSM.States;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.ConsultTheCard;
using KnockBox.Services.State.Games.ConsultTheCard.Data;
using KnockBox.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.ConsultTheCardTests.Unit.Logic.Games.ConsultTheCard.States
{
    [TestClass]
    public class CluePhaseStateTests
    {
        private Mock<IRandomNumberService> _rng = default!;
        private Mock<ILogger> _logger = default!;
        private Mock<ILogger<ConsultTheCardGameState>> _stateLogger = default!;
        private ConsultTheCardGameState _state = default!;
        private ConsultTheCardGameContext _context = default!;

        [TestInitialize]
        public void Setup()
        {
            _rng = new Mock<IRandomNumberService>();
            _rng.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<RandomType>()))
                .Returns(0);
            _rng.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<RandomType>()))
                .Returns(0);
            _logger = new Mock<ILogger>();
            _stateLogger = new Mock<ILogger<ConsultTheCardGameState>>();

            var host = new User("Host", "host-id");
            _state = new ConsultTheCardGameState(host, _stateLogger.Object);
            _context = new ConsultTheCardGameContext(_state, _rng.Object, _logger.Object);

            // Add 4 players with roles assigned.
            AddPlayer("p0", "Player 0", Role.Agent, "Ocean");
            AddPlayer("p1", "Player 1", Role.Agent, "Ocean");
            AddPlayer("p2", "Player 2", Role.Agent, "Ocean");
            AddPlayer("p3", "Player 3", Role.Insider, "Lake");
            _state.CurrentWordPair = ["Ocean", "Lake"];
        }

        private void AddPlayer(string id, string name, Role role, string? secretWord)
        {
            var ps = new ConsultTheCardPlayerState
            {
                PlayerId = id,
                DisplayName = name,
                Role = role,
                SecretWord = secretWord
            };
            _state.GamePlayers[id] = ps;
            _state.TurnManager.TurnOrder.Add(id);
        }

        [TestMethod]
        public void OnEnter_SetsPhaseToCluePhase()
        {
            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);
            Assert.AreEqual(ConsultTheCardGamePhase.CluePhase, _state.Phase);
        }

        [TestMethod]
        public void OnEnter_ResetsCycleState()
        {
            _state.GamePlayers["p0"].HasSubmittedClue = true;
            _state.GamePlayers["p0"].CurrentClue = "wave";
            _state.LastElimination = new EliminationResult("", "", default, WasTie: true);

            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);

            Assert.IsFalse(_state.GamePlayers["p0"].HasSubmittedClue);
            Assert.IsNull(_state.GamePlayers["p0"].CurrentClue);
            Assert.IsNull(_state.LastElimination);
        }

        [TestMethod]
        public void OnEnter_AdvancesToAlivePlayer()
        {
            // Eliminate p0 so the first alive player is p1.
            _state.GamePlayers["p0"].IsEliminated = true;
            _state.TurnManager.SetCurrentPlayerIndex(0);

            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);

            string currentPlayer = _state.TurnManager.TurnOrder[_state.TurnManager.CurrentPlayerIndex];
            Assert.AreNotEqual("p0", currentPlayer, "Should skip eliminated player.");
        }

        [TestMethod]
        public void OnEnter_RotatingStartPlayer()
        {
            // Set index to 2 (simulating previous cycle ended at index 2).
            _state.TurnManager.SetCurrentPlayerIndex(2);

            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);

            // Should start at index 2 (p2) since p2 is alive.
            Assert.AreEqual(2, _state.TurnManager.CurrentPlayerIndex);
        }

        [TestMethod]
        public void HandleCommand_ValidClue_StoresAndAdvances()
        {
            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);

            string currentPlayer = _state.TurnManager.TurnOrder[_state.TurnManager.CurrentPlayerIndex];
            var result = clueState.HandleCommand(_context, new SubmitClueCommand(currentPlayer, "wave"));

            Assert.IsTrue(result.IsSuccess);
            var player = _context.GetPlayer(currentPlayer)!;
            Assert.IsTrue(player.HasSubmittedClue);
            Assert.AreEqual("wave", player.CurrentClue);
            Assert.IsTrue(_state.UsedClues.Contains("wave"));
        }

        [TestMethod]
        public void HandleCommand_AcceptsClueWithSpaces()
        {
            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);

            string currentPlayer = _state.TurnManager.TurnOrder[_state.TurnManager.CurrentPlayerIndex];
            var result = clueState.HandleCommand(_context, new SubmitClueCommand(currentPlayer, "two words"));

            Assert.IsTrue(result.IsSuccess);
            var player = _context.GetPlayer(currentPlayer)!;
            Assert.IsTrue(player.HasSubmittedClue);
            Assert.AreEqual("two words", player.CurrentClue);
        }

        [TestMethod]
        public void HandleCommand_RejectsSecretWord()
        {
            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);

            string currentPlayer = _state.TurnManager.TurnOrder[_state.TurnManager.CurrentPlayerIndex];
            var player = _context.GetPlayer(currentPlayer)!;
            var result = clueState.HandleCommand(_context, new SubmitClueCommand(currentPlayer, player.SecretWord!));

            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void HandleCommand_RejectsAlreadyUsedClue()
        {
            _state.UsedClues.Add("wave");

            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);

            string currentPlayer = _state.TurnManager.TurnOrder[_state.TurnManager.CurrentPlayerIndex];
            var result = clueState.HandleCommand(_context, new SubmitClueCommand(currentPlayer, "wave"));

            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void HandleCommand_RejectsWrongPlayer()
        {
            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);

            string currentPlayer = _state.TurnManager.TurnOrder[_state.TurnManager.CurrentPlayerIndex];
            string wrongPlayer = _state.TurnManager.TurnOrder[(_state.TurnManager.CurrentPlayerIndex + 1) % _state.TurnManager.TurnOrder.Count];

            var result = clueState.HandleCommand(_context, new SubmitClueCommand(wrongPlayer, "wave"));
            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void HandleCommand_AllCluesSubmitted_TransitionsToDiscussion()
        {
            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);

            // Submit clues for all alive players in turn order.
            string[] clues = ["wave", "splash", "tide", "fish"];
            for (int i = 0; i < 4; i++)
            {
                string currentPlayer = _state.TurnManager.TurnOrder[_state.TurnManager.CurrentPlayerIndex];
                var result = clueState.HandleCommand(_context, new SubmitClueCommand(currentPlayer, clues[i]));
                Assert.IsTrue(result.IsSuccess);

                if (i < 3)
                    Assert.IsNull(result.Value, $"Should not transition after clue {i}");
                else
                    Assert.IsInstanceOfType<DiscussionPhaseState>(result.Value);
            }
        }

        [TestMethod]
        public void HandleCommand_SkipsEliminatedPlayers()
        {
            // Eliminate p1 (index 1 in turn order).
            _state.GamePlayers["p1"].IsEliminated = true;
            _state.TurnManager.SetCurrentPlayerIndex(0);

            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);

            // Submit clue for p0.
            var result = clueState.HandleCommand(_context, new SubmitClueCommand("p0", "wave"));
            Assert.IsTrue(result.IsSuccess);

            // Next player should be p2, not p1 (eliminated).
            string next = _state.TurnManager.TurnOrder[_state.TurnManager.CurrentPlayerIndex];
            Assert.AreEqual("p2", next, "Should skip eliminated p1.");
        }

        [TestMethod]
        public void Tick_WithTimersEnabled_AutoSubmitsOnTimeout()
        {
            _state.Config.EnableTimers = true;
            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);

            string currentPlayer = _state.TurnManager.TurnOrder[_state.TurnManager.CurrentPlayerIndex];

            // Tick after timeout.
            var result = clueState.Tick(_context, DateTimeOffset.UtcNow.AddMinutes(5));
            Assert.IsTrue(result.IsSuccess);

            var player = _context.GetPlayer(currentPlayer)!;
            Assert.IsTrue(player.HasSubmittedClue);
            Assert.AreEqual("...", player.CurrentClue);
        }

        [TestMethod]
        public void Tick_WithTimersDisabled_DoesNotAutoSubmit()
        {
            _state.Config.EnableTimers = false;
            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);

            var result = clueState.Tick(_context, DateTimeOffset.UtcNow.AddMinutes(5));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsNull(result.Value);

            string currentPlayer = _state.TurnManager.TurnOrder[_state.TurnManager.CurrentPlayerIndex];
            var player = _context.GetPlayer(currentPlayer)!;
            Assert.IsFalse(player.HasSubmittedClue);
        }

        [TestMethod]
        public void HandleCommand_CaseInsensitiveSecretWordCheck()
        {
            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);

            string currentPlayer = _state.TurnManager.TurnOrder[_state.TurnManager.CurrentPlayerIndex];
            // Secret word is "Ocean"; try lowercase.
            var result = clueState.HandleCommand(_context, new SubmitClueCommand(currentPlayer, "ocean"));
            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void HandleCommand_CaseInsensitiveUsedClueCheck()
        {
            _state.UsedClues.Add("Wave");

            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);

            string currentPlayer = _state.TurnManager.TurnOrder[_state.TurnManager.CurrentPlayerIndex];
            var result = clueState.HandleCommand(_context, new SubmitClueCommand(currentPlayer, "wave"));
            Assert.IsFalse(result.IsSuccess);
        }

        // ── PendingClue auto-submit tests ─────────────────────────────────────

        [TestMethod]
        public void Tick_WithPendingClue_AutoSubmitsPendingText()
        {
            _state.Config.EnableTimers = true;
            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);

            string currentPlayer = _state.TurnManager.TurnOrder[_state.TurnManager.CurrentPlayerIndex];
            var player = _context.GetPlayer(currentPlayer)!;
            player.PendingClue = "my pending clue";

            clueState.Tick(_context, DateTimeOffset.UtcNow.AddMinutes(5));

            Assert.IsTrue(player.HasSubmittedClue);
            Assert.AreEqual("my pending clue", player.CurrentClue);
        }

        [TestMethod]
        public void Tick_WithEmptyPendingClue_FallsBackToEllipsis()
        {
            _state.Config.EnableTimers = true;
            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);

            string currentPlayer = _state.TurnManager.TurnOrder[_state.TurnManager.CurrentPlayerIndex];
            var player = _context.GetPlayer(currentPlayer)!;
            player.PendingClue = "   "; // whitespace-only

            clueState.Tick(_context, DateTimeOffset.UtcNow.AddMinutes(5));

            Assert.IsTrue(player.HasSubmittedClue);
            Assert.AreEqual("...", player.CurrentClue);
        }

        [TestMethod]
        public void Tick_WithPendingClueMatchingSecretWord_FallsBackToEllipsis()
        {
            _state.Config.EnableTimers = true;
            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);

            string currentPlayer = _state.TurnManager.TurnOrder[_state.TurnManager.CurrentPlayerIndex];
            var player = _context.GetPlayer(currentPlayer)!;
            player.PendingClue = player.SecretWord; // "Ocean"

            clueState.Tick(_context, DateTimeOffset.UtcNow.AddMinutes(5));

            Assert.IsTrue(player.HasSubmittedClue);
            Assert.AreEqual("...", player.CurrentClue);
        }

        [TestMethod]
        public void Tick_WithPendingClueAlreadyUsed_FallsBackToEllipsis()
        {
            _state.Config.EnableTimers = true;
            _state.UsedClues.Add("wave");

            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);

            string currentPlayer = _state.TurnManager.TurnOrder[_state.TurnManager.CurrentPlayerIndex];
            var player = _context.GetPlayer(currentPlayer)!;
            player.PendingClue = "wave";

            clueState.Tick(_context, DateTimeOffset.UtcNow.AddMinutes(5));

            Assert.IsTrue(player.HasSubmittedClue);
            Assert.AreEqual("...", player.CurrentClue);
        }

        [TestMethod]
        public void Tick_WithNoPendingClue_FallsBackToEllipsis()
        {
            _state.Config.EnableTimers = true;
            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);

            string currentPlayer = _state.TurnManager.TurnOrder[_state.TurnManager.CurrentPlayerIndex];
            var player = _context.GetPlayer(currentPlayer)!;
            player.PendingClue = null;

            clueState.Tick(_context, DateTimeOffset.UtcNow.AddMinutes(5));

            Assert.IsTrue(player.HasSubmittedClue);
            Assert.AreEqual("...", player.CurrentClue);
        }
    }
}
