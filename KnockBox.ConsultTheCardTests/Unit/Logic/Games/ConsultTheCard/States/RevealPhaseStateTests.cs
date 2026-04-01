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
    public class RevealPhaseStateTests
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

            AddPlayer("p0", "Player 0", Role.Agent, "Ocean");
            AddPlayer("p1", "Player 1", Role.Agent, "Ocean");
            AddPlayer("p2", "Player 2", Role.Insider, "Lake");
            AddPlayer("p3", "Player 3", Role.Agent, "Ocean");
            _state.CurrentWordPair = ["Ocean", "Lake"];
        }

        private void AddPlayer(string id, string name, Role role, string? secretWord)
        {
            _state.GamePlayers[id] = new ConsultTheCardPlayerState
            {
                PlayerId = id,
                DisplayName = name,
                Role = role,
                SecretWord = secretWord
            };
            _state.TurnManager.TurnOrder.Add(id);
        }

        [TestMethod]
        public void OnEnter_SetsPhaseToReveal()
        {
            // Set up a tie (no elimination).
            _state.LastElimination = new EliminationResult("", "", default, WasTie: true);

            var reveal = new RevealPhaseState();
            reveal.OnEnter(_context);
            Assert.AreEqual(ConsultTheCardGamePhase.Reveal, _state.Phase);
        }

        [TestMethod]
        public void OnEnter_CallsApplyCycleScoring()
        {
            // Set up a voter who voted for an Agent.
            _state.GamePlayers["p2"].HasVoted = true;
            _state.GamePlayers["p2"].VoteTargetId = "p0"; // Agent
            _state.LastElimination = new EliminationResult("", "", default, WasTie: true);

            var reveal = new RevealPhaseState();
            reveal.OnEnter(_context);

            // p2 (Insider) survives round → +1 score. Note: only Agents lose points for voting for an Agent.
            Assert.AreEqual(1, _state.GamePlayers["p2"].Score);
        }

        [TestMethod]
        public void OnEnter_NonInformantEliminated_ChecksWinConditions()
        {
            // Eliminate p2 (Insider) — leaving only 3 players alive, game continues.
            _state.GamePlayers["p2"].IsEliminated = true;
            _state.LastElimination = new EliminationResult("p2", "Player 2", Role.Insider, WasTie: false);

            var reveal = new RevealPhaseState();
            var result = reveal.OnEnter(_context);

            // 3 alive players, game should continue.
            Assert.IsTrue(result.IsSuccess);
            Assert.IsNull(result.Value);
        }

        [TestMethod]
        public void OnEnter_NonInformantEliminated_TwoRemaining_TransitionsToGameOver()
        {
            // Start with 3 players, eliminate one to get to 2.
            _state.GamePlayers.TryRemove("p3", out _);
            _state.TurnManager.TurnOrder.Remove("p3");
            _state.GamePlayers["p2"].IsEliminated = true;
            _state.LastElimination = new EliminationResult("p2", "Player 2", Role.Insider, WasTie: false);

            var reveal = new RevealPhaseState();
            var result = reveal.OnEnter(_context);

            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<GameOverState>(result.Value);
        }

        [TestMethod]
        public void OnEnter_InformantEliminated_SetsAwaitingInformantGuess()
        {
            // Add Informant and eliminate them.
            AddPlayer("p4", "Player 4", Role.Informant, null);
            _state.GamePlayers["p4"].IsEliminated = true;
            _state.LastElimination = new EliminationResult("p4", "Player 4", Role.Informant, WasTie: false);

            var reveal = new RevealPhaseState();
            reveal.OnEnter(_context);

            Assert.IsTrue(_state.AwaitingInformantGuess);
        }

        [TestMethod]
        public void HandleCommand_InformantCorrectGuess_TransitionsToGameOver()
        {
            AddPlayer("p4", "Player 4", Role.Informant, null);
            _state.GamePlayers["p4"].IsEliminated = true;
            _state.LastElimination = new EliminationResult("p4", "Player 4", Role.Informant, WasTie: false);

            var reveal = new RevealPhaseState();
            reveal.OnEnter(_context);

            var result = reveal.HandleCommand(_context, new InformantGuessCommand("p4", "Ocean"));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<GameOverState>(result.Value);
            Assert.AreEqual(Role.Informant, _state.WinResult!.WinningTeam);
        }

        [TestMethod]
        public void HandleCommand_InformantWrongGuess_ContinuesGame()
        {
            AddPlayer("p4", "Player 4", Role.Informant, null);
            _state.GamePlayers["p4"].IsEliminated = true;
            _state.LastElimination = new EliminationResult("p4", "Player 4", Role.Informant, WasTie: false);

            var reveal = new RevealPhaseState();
            reveal.OnEnter(_context);

            var result = reveal.HandleCommand(_context, new InformantGuessCommand("p4", "Wrong"));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsNull(result.Value); // Game continues (null = stay in current state).

            Assert.IsFalse(_state.AwaitingInformantGuess);
            // LastInformantGuess is cleared by ResetEliminationCycleState.
            // The guess was recorded and win conditions checked before the reset.
        }

        [TestMethod]
        public void HandleCommand_NonInformant_CannotGuess()
        {
            AddPlayer("p4", "Player 4", Role.Informant, null);
            _state.GamePlayers["p4"].IsEliminated = true;
            _state.LastElimination = new EliminationResult("p4", "Player 4", Role.Informant, WasTie: false);

            var reveal = new RevealPhaseState();
            reveal.OnEnter(_context);

            // p0 is not the Informant.
            var result = reveal.HandleCommand(_context, new InformantGuessCommand("p0", "Ocean"));
            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void HandleCommand_GuessWhenNotAwaiting_Rejected()
        {
            _state.LastElimination = new EliminationResult("", "", default, WasTie: true);

            var reveal = new RevealPhaseState();
            reveal.OnEnter(_context);

            var result = reveal.HandleCommand(_context, new InformantGuessCommand("p0", "Ocean"));
            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void Tick_AwaitingInformantGuess_TimeoutForfeit()
        {
            AddPlayer("p4", "Player 4", Role.Informant, null);
            _state.GamePlayers["p4"].IsEliminated = true;
            _state.LastElimination = new EliminationResult("p4", "Player 4", Role.Informant, WasTie: false);

            var reveal = new RevealPhaseState();
            reveal.OnEnter(_context);

            Assert.IsTrue(_state.AwaitingInformantGuess);

            // Tick after informant guess timeout.
            var result = reveal.Tick(_context, DateTimeOffset.UtcNow.AddMinutes(5));
            Assert.IsTrue(result.IsSuccess);

            Assert.IsFalse(_state.AwaitingInformantGuess);
            // LastInformantGuess is cleared by ResetEliminationCycleState.
            // The guess result was recorded and win conditions checked before the reset.
        }

        [TestMethod]
        public void Tick_NoInformantGuess_AutoAdvancesToCluePhase()
        {
            _state.LastElimination = new EliminationResult("", "", default, WasTie: true);

            var reveal = new RevealPhaseState();
            reveal.OnEnter(_context);

            var result = reveal.Tick(_context, DateTimeOffset.UtcNow.AddMinutes(5));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<CluePhaseState>(result.Value);
        }

        [TestMethod]
        public void Tick_BeforeTimeout_ReturnsNull()
        {
            _state.LastElimination = new EliminationResult("", "", default, WasTie: true);

            var reveal = new RevealPhaseState();
            reveal.OnEnter(_context);

            var result = reveal.Tick(_context, DateTimeOffset.UtcNow);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsNull(result.Value);
        }

        [TestMethod]
        public void HandleCommand_InformantGuessCaseInsensitive()
        {
            AddPlayer("p4", "Player 4", Role.Informant, null);
            _state.GamePlayers["p4"].IsEliminated = true;
            _state.LastElimination = new EliminationResult("p4", "Player 4", Role.Informant, WasTie: false);

            var reveal = new RevealPhaseState();
            reveal.OnEnter(_context);

            // "ocean" vs "Ocean" - should be case insensitive.
            var result = reveal.HandleCommand(_context, new InformantGuessCommand("p4", "ocean"));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<GameOverState>(result.Value);
        }

        [TestMethod]
        public void HandleCommand_InformantWrongGuess_TwoRemaining_TransitionsToGameOver()
        {
            // Remove p2 and p3 from game state, add p4 as Informant.
            // Remaining: p0 (Agent), p1 (Agent), p4 (Informant).
            _state.GamePlayers.TryRemove("p2", out _);
            _state.TurnManager.TurnOrder.Remove("p2");
            _state.GamePlayers.TryRemove("p3", out _);
            _state.TurnManager.TurnOrder.Remove("p3");
            AddPlayer("p4", "Player 4", Role.Informant, null);

            // p4 (Informant) is eliminated; p0 and p1 remain alive.
            _state.GamePlayers["p4"].IsEliminated = true;
            _state.LastElimination = new EliminationResult("p4", "Player 4", Role.Informant, WasTie: false);

            var reveal = new RevealPhaseState();
            reveal.OnEnter(_context);

            Assert.IsTrue(_state.AwaitingInformantGuess);

            // Wrong guess — only 2 alive players (p0, p1) remain → game should end.
            var result = reveal.HandleCommand(_context, new InformantGuessCommand("p4", "WrongWord"));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<GameOverState>(result.Value);
        }

        [TestMethod]
        public void OnEnter_LastInsiderEliminated_MoreThanTwoRemaining_GameContinues()
        {
            // 4 players: p0 (Agent), p1 (Agent), p2 (Insider, eliminated), p3 (Agent).
            // 3 alive after elimination — game should continue.
            _state.GamePlayers["p2"].IsEliminated = true;
            _state.LastElimination = new EliminationResult("p2", "Player 2", Role.Insider, WasTie: false);

            var reveal = new RevealPhaseState();
            var result = reveal.OnEnter(_context);

            Assert.IsTrue(result.IsSuccess);
            Assert.IsNull(result.Value, "Game should continue when >2 players alive after Insider eliminated.");
        }

        [TestMethod]
        public void Tick_AwaitingInformantGuess_Timeout_TwoRemaining_TransitionsToGameOver()
        {
            // Set up: 3 players total, Informant eliminated, 2 alive remain.
            _state.GamePlayers.TryRemove("p2", out _);
            _state.TurnManager.TurnOrder.Remove("p2");
            _state.GamePlayers.TryRemove("p3", out _);
            _state.TurnManager.TurnOrder.Remove("p3");
            AddPlayer("p4", "Player 4", Role.Informant, null);

            _state.GamePlayers["p4"].IsEliminated = true;
            _state.LastElimination = new EliminationResult("p4", "Player 4", Role.Informant, WasTie: false);

            var reveal = new RevealPhaseState();
            reveal.OnEnter(_context);

            Assert.IsTrue(_state.AwaitingInformantGuess);

            // Timeout → forfeit → only 2 alive → game over.
            var result = reveal.Tick(_context, DateTimeOffset.UtcNow.AddMinutes(5));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<GameOverState>(result.Value);
        }

        [TestMethod]
        public void OnEnter_PerCycleScoringApplied_OnBothEliminationAndTie()
        {
            // Test that scoring is applied for a real elimination too.
            _state.GamePlayers["p2"].HasVoted = true;
            _state.GamePlayers["p2"].VoteTargetId = "p0"; // Agent
            _state.GamePlayers["p3"].IsEliminated = true;
            _state.LastElimination = new EliminationResult("p3", "Player 3", Role.Agent, WasTie: false);

            var reveal = new RevealPhaseState();
            reveal.OnEnter(_context);

            // p2 (Insider) survives the round since they weren't eliminated → +1 score.
            Assert.AreEqual(1, _state.GamePlayers["p2"].Score);
        }
    }
}
