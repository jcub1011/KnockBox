using KnockBox.Codeword.Services.Logic.Games.FSM;
using KnockBox.Codeword.Services.Logic.Games.FSM.States;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Codeword.Services.State.Games;
using KnockBox.Codeword.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.Codeword.Tests.Unit.Logic.Games.Codeword.States
{
    [TestClass]
    public class CluePhaseStateTests
    {
        private Mock<IRandomNumberService> _rng = default!;
        private Mock<ILogger> _logger = default!;
        private Mock<ILogger<CodewordGameState>> _stateLogger = default!;
        private CodewordGameState _state = default!;
        private CodewordGameContext _context = default!;

        private Guid _p0Id = default!;
        private Guid _p1Id = default!;
        private Guid _p2Id = default!;
        private Guid _p3Id = default!;

        [TestInitialize]
        public void Setup()
        {
            _p0Id = Guid.NewGuid();
            _p1Id = Guid.NewGuid();
            _p2Id = Guid.NewGuid();
            _p3Id = Guid.NewGuid();

            _rng = new Mock<IRandomNumberService>();
            _rng.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<RandomType>()))
                .Returns(0);
            _rng.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<RandomType>()))
                .Returns(0);
            _logger = new Mock<ILogger>();
            _stateLogger = new Mock<ILogger<CodewordGameState>>();

            var host = UserFactory.Create("Host", Guid.NewGuid());
            _state = new CodewordGameState(host, _stateLogger.Object);
            _context = new CodewordGameContext(_state, _rng.Object, _logger.Object);

            // Add 4 players with roles assigned.
            AddPlayer(_p0Id, "Player 0", Role.Agent, "Ocean");
            AddPlayer(_p1Id, "Player 1", Role.Agent, "Ocean");
            AddPlayer(_p2Id, "Player 2", Role.Agent, "Ocean");
            AddPlayer(_p3Id, "Player 3", Role.Insider, "Lake");
            _state.CurrentWordPair = ["Ocean", "Lake"];
        }

        private void AddPlayer(Guid id, string name, Role role, string? secretWord)
        {
            var ps = new CodewordPlayerState
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
            Assert.AreEqual(CodewordGamePhase.CluePhase, _state.Phase);
        }

        [TestMethod]
        public void OnEnter_ResetsCycleState()
        {
            _state.GamePlayers[_p0Id].HasSubmittedClue = true;
            _state.GamePlayers[_p0Id].CurrentClue = "wave";
            _state.LastElimination = new EliminationResult(Guid.Empty, "", default, WasTie: true);

            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);

            Assert.IsFalse(_state.GamePlayers[_p0Id].HasSubmittedClue);
            Assert.IsNull(_state.GamePlayers[_p0Id].CurrentClue);
            Assert.IsNull(_state.LastElimination);
        }

        [TestMethod]
        public void OnEnter_AdvancesToAlivePlayer()
        {
            // Eliminate p0 so the first alive player is p1.
            _state.GamePlayers[_p0Id].IsEliminated = true;
            _state.TurnManager.SetCurrentPlayerIndex(0);

            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);

            Guid currentPlayer = _state.TurnManager.TurnOrder[_state.TurnManager.CurrentPlayerIndex];
            Assert.AreNotEqual(_p0Id, currentPlayer, "Should skip eliminated player.");
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

            Guid currentPlayer = _state.TurnManager.TurnOrder[_state.TurnManager.CurrentPlayerIndex];
            var result = clueState.HandleCommand(_context, new SubmitClueCommand(currentPlayer, "wave"));

            Assert.IsTrue(result.IsSuccess);
            var player = _context.GetPlayer(currentPlayer)!;
            Assert.IsTrue(player.HasSubmittedClue);
            Assert.AreEqual("wave", player.CurrentClue);
            Assert.IsTrue(_state.UsedClues.ContainsKey("wave"));
        }

        [TestMethod]
        public void HandleCommand_AcceptsClueWithSpaces()
        {
            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);

            Guid currentPlayer = _state.TurnManager.TurnOrder[_state.TurnManager.CurrentPlayerIndex];
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

            Guid currentPlayer = _state.TurnManager.TurnOrder[_state.TurnManager.CurrentPlayerIndex];
            var player = _context.GetPlayer(currentPlayer)!;
            var result = clueState.HandleCommand(_context, new SubmitClueCommand(currentPlayer, player.SecretWord!));

            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void HandleCommand_RejectsAlreadyUsedClue()
        {
            _state.UsedClues["wave"] = "SomePlayer";

            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);

            Guid currentPlayer = _state.TurnManager.TurnOrder[_state.TurnManager.CurrentPlayerIndex];
            var result = clueState.HandleCommand(_context, new SubmitClueCommand(currentPlayer, "wave"));

            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void HandleCommand_RejectsWrongPlayer()
        {
            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);

            Guid currentPlayer = _state.TurnManager.TurnOrder[_state.TurnManager.CurrentPlayerIndex];
            Guid wrongPlayer = _state.TurnManager.TurnOrder[(_state.TurnManager.CurrentPlayerIndex + 1) % _state.TurnManager.TurnOrder.Count];

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
                Guid currentPlayer = _state.TurnManager.TurnOrder[_state.TurnManager.CurrentPlayerIndex];
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
            _state.GamePlayers[_p1Id].IsEliminated = true;
            _state.TurnManager.SetCurrentPlayerIndex(0);

            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);

            // Submit clue for p0.
            var result = clueState.HandleCommand(_context, new SubmitClueCommand(_p0Id, "wave"));
            Assert.IsTrue(result.IsSuccess);

            // Next player should be p2, not p1 (eliminated).
            Guid next = _state.TurnManager.TurnOrder[_state.TurnManager.CurrentPlayerIndex];
            Assert.AreEqual(_p2Id, next, "Should skip eliminated p1.");
        }

        [TestMethod]
        public void Tick_WithTimersEnabled_AutoSubmitsOnTimeout()
        {
            _state.UpdateSettings(s => s with { EnableTimers = true });
            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);

            Guid currentPlayer = _state.TurnManager.TurnOrder[_state.TurnManager.CurrentPlayerIndex];

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
            _state.UpdateSettings(s => s with { EnableTimers = false });
            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);

            var result = clueState.Tick(_context, DateTimeOffset.UtcNow.AddMinutes(5));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsNull(result.Value);

            Guid currentPlayer = _state.TurnManager.TurnOrder[_state.TurnManager.CurrentPlayerIndex];
            var player = _context.GetPlayer(currentPlayer)!;
            Assert.IsFalse(player.HasSubmittedClue);
        }

        [TestMethod]
        public void HandleCommand_CaseInsensitiveSecretWordCheck()
        {
            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);

            Guid currentPlayer = _state.TurnManager.TurnOrder[_state.TurnManager.CurrentPlayerIndex];
            // Secret word is "Ocean"; try lowercase.
            var result = clueState.HandleCommand(_context, new SubmitClueCommand(currentPlayer, "ocean"));
            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void HandleCommand_CaseInsensitiveUsedClueCheck()
        {
            _state.UsedClues["Wave"] = "SomePlayer";

            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);

            Guid currentPlayer = _state.TurnManager.TurnOrder[_state.TurnManager.CurrentPlayerIndex];
            var result = clueState.HandleCommand(_context, new SubmitClueCommand(currentPlayer, "wave"));
            Assert.IsFalse(result.IsSuccess);
        }

        // ── Timeout auto-submit ───────────────────────────────────────────────
        // In the WASM model the clue input is client-owned (the server no longer keeps a
        // per-keystroke PendingClue buffer). The active player's own browser auto-submits
        // its draft just before the deadline; the server's Tick is the fallback for a player
        // who sent nothing by the buzzer and always submits "...".

        [TestMethod]
        public void Tick_OnTimeout_AutoSubmitsEllipsisForNonSubmittingPlayer()
        {
            _state.UpdateSettings(s => s with { EnableTimers = true });
            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);

            Guid currentPlayer = _state.TurnManager.TurnOrder[_state.TurnManager.CurrentPlayerIndex];
            var player = _context.GetPlayer(currentPlayer)!;

            clueState.Tick(_context, DateTimeOffset.UtcNow.AddMinutes(5));

            Assert.IsTrue(player.HasSubmittedClue);
            Assert.AreEqual("...", player.CurrentClue);
            Assert.IsTrue(
                _state.CurrentRoundClues.Any(c => c.PlayerId == currentPlayer && c.Clue == "..."),
                "The timed-out player's auto-submitted clue must appear in the round's clue list.");
        }

        [TestMethod]
        public void Tick_BeforeDeadline_DoesNotAutoSubmit()
        {
            _state.UpdateSettings(s => s with { EnableTimers = true });
            var clueState = new CluePhaseState();
            clueState.OnEnter(_context);

            Guid currentPlayer = _state.TurnManager.TurnOrder[_state.TurnManager.CurrentPlayerIndex];
            var player = _context.GetPlayer(currentPlayer)!;

            clueState.Tick(_context, DateTimeOffset.UtcNow);

            Assert.IsFalse(player.HasSubmittedClue);
        }
    }
}
