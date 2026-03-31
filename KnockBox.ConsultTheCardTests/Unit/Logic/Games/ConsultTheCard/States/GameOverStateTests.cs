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
    public class GameOverStateTests
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
            _state.TurnOrder.Add(id);
        }

        [TestMethod]
        public void OnEnter_SetsPhaseToGameOver()
        {
            _state.WinResult = new WinConditionResult(true, Role.Agent, "Test");

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);

            Assert.AreEqual(ConsultTheCardGamePhase.GameOver, _state.GamePhase);
        }

        [TestMethod]
        public void OnEnter_AppliesEndOfGameScoring()
        {
            _state.WinResult = new WinConditionResult(true, Role.Agent, "Test");

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);

            // p0: Agent, alive → +2 survive + +1 winning team = 3.
            Assert.AreEqual(3, _state.GamePlayers["p0"].Score);
        }

        [TestMethod]
        public void OnEnter_AccumulatesScoresToGameScores()
        {
            _state.WinResult = new WinConditionResult(true, Role.Agent, "Test");

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);

            Assert.IsTrue(_state.GameScores.ContainsKey("p0"));
            Assert.IsTrue(_state.GameScores["p0"] > 0);
        }

        [TestMethod]
        public void HandleCommand_StartNextGame_HostOnly()
        {
            _state.Config.TotalGames = 5;
            _state.WinResult = new WinConditionResult(true, Role.Agent, "Test");

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);

            var result = gameOver.HandleCommand(_context, new StartNextGameCommand("host-id"));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<SetupState>(result.Value);
        }

        [TestMethod]
        public void HandleCommand_StartNextGame_RejectsNonHost()
        {
            _state.Config.TotalGames = 5;
            _state.WinResult = new WinConditionResult(true, Role.Agent, "Test");

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);

            var result = gameOver.HandleCommand(_context, new StartNextGameCommand("p0"));
            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void HandleCommand_StartNextGame_IncrementsGameNumber()
        {
            _state.Config.TotalGames = 5;
            _state.CurrentGameNumber = 1;
            _state.WinResult = new WinConditionResult(true, Role.Agent, "Test");

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);

            gameOver.HandleCommand(_context, new StartNextGameCommand("host-id"));
            Assert.AreEqual(2, _state.CurrentGameNumber);
        }

        [TestMethod]
        public void HandleCommand_StartNextGame_ClearsPlayerState()
        {
            _state.Config.TotalGames = 5;
            _state.WinResult = new WinConditionResult(true, Role.Agent, "Test");

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);
            gameOver.HandleCommand(_context, new StartNextGameCommand("host-id"));

            foreach (var ps in _state.GamePlayers.Values)
            {
                Assert.AreEqual(default(Role), ps.Role);
                Assert.IsNull(ps.SecretWord);
                Assert.IsFalse(ps.IsEliminated);
                Assert.IsFalse(ps.HasSubmittedClue);
                Assert.IsNull(ps.CurrentClue);
                Assert.IsNull(ps.VoteTargetId);
                Assert.IsFalse(ps.HasVoted);
                Assert.IsFalse(ps.HasVotedToEndGame);
                Assert.AreEqual(0, ps.Score);
            }
        }

        [TestMethod]
        public void HandleCommand_StartNextGame_ClearsGameLevelState()
        {
            _state.Config.TotalGames = 5;
            _state.UsedClues.Add("test");
            _state.CurrentRoundClues.Add(new ClueEntry("p0", "P0", "test"));
            _state.CurrentRoundVotes.Add(new VoteEntry("p0", "P0", "p1", "P1"));
            _state.WinResult = new WinConditionResult(true, Role.Agent, "Test");

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);
            gameOver.HandleCommand(_context, new StartNextGameCommand("host-id"));

            Assert.AreEqual(0, _state.CurrentEliminationCycle);
            Assert.AreEqual(0, _state.CurrentCluePlayerIndex);
            Assert.IsNull(_state.CurrentWordPair);
            Assert.AreEqual(0, _state.CurrentRoundClues.Count);
            Assert.AreEqual(0, _state.CurrentRoundVotes.Count);
            Assert.AreEqual(0, _state.UsedClues.Count);
            Assert.IsNull(_state.LastElimination);
            Assert.IsNull(_state.LastInformantGuess);
            Assert.IsFalse(_state.AwaitingInformantGuess);
            Assert.IsNull(_state.WinResult);
        }

        [TestMethod]
        public void HandleCommand_StartNextGame_PreservesUsedWordPairIndices()
        {
            _state.Config.TotalGames = 5;
            _context.UsedWordPairIndices.Add(0);
            _context.UsedWordPairIndices.Add(1);
            _state.WinResult = new WinConditionResult(true, Role.Agent, "Test");

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);
            gameOver.HandleCommand(_context, new StartNextGameCommand("host-id"));

            Assert.AreEqual(2, _context.UsedWordPairIndices.Count);
        }

        [TestMethod]
        public void HandleCommand_StartNextGame_PreservesGameScores()
        {
            _state.Config.TotalGames = 5;
            _state.WinResult = new WinConditionResult(true, Role.Agent, "Test");

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);

            // Scores accumulated in OnEnter.
            int p0Score = _state.GameScores.GetValueOrDefault("p0", 0);

            gameOver.HandleCommand(_context, new StartNextGameCommand("host-id"));

            Assert.AreEqual(p0Score, _state.GameScores["p0"]);
        }

        [TestMethod]
        public void HandleCommand_StartNextGame_RejectsWhenAllGamesPlayed()
        {
            _state.Config.TotalGames = 1;
            _state.CurrentGameNumber = 1;
            _state.WinResult = new WinConditionResult(true, Role.Agent, "Test");

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);

            var result = gameOver.HandleCommand(_context, new StartNextGameCommand("host-id"));
            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void HandleCommand_ReturnToLobby_HostOnly()
        {
            _state.WinResult = new WinConditionResult(true, Role.Agent, "Test");

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);

            var result = gameOver.HandleCommand(_context, new ReturnToLobbyCommand("host-id"));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsNull(result.Value); // Null signals lobby transition.
        }

        [TestMethod]
        public void HandleCommand_ReturnToLobby_RejectsNonHost()
        {
            _state.WinResult = new WinConditionResult(true, Role.Agent, "Test");

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);

            var result = gameOver.HandleCommand(_context, new ReturnToLobbyCommand("p0"));
            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void OnEnter_EvaluatesWinConditionsIfNotSet()
        {
            // WinResult is null; GameOverState should evaluate it.
            _state.WinResult = null;
            // With only 2 alive players (eliminate p2), the game ends.
            _state.GamePlayers["p2"].IsEliminated = true;

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);

            Assert.IsNotNull(_state.WinResult);
            Assert.IsTrue(_state.WinResult.GameOver);
        }

        [TestMethod]
        public void MultiGame_CumulativeScoresTrackedCorrectly()
        {
            _state.Config.TotalGames = 3;
            _state.WinResult = new WinConditionResult(true, Role.Agent, "Test");

            // Game 1: Apply scoring.
            var gameOver1 = new GameOverState();
            gameOver1.OnEnter(_context);

            // p0: Agent, alive → +2 + +1 = 3. GameScores["p0"] = 3.
            int game1Score = _state.GameScores.GetValueOrDefault("p0", 0);
            Assert.IsTrue(game1Score > 0, "Game 1 score should be > 0.");

            // Start next game → resets player Score but preserves GameScores.
            gameOver1.HandleCommand(_context, new StartNextGameCommand("host-id"));
            Assert.AreEqual(0, _state.GamePlayers["p0"].Score, "Player score should reset after StartNextGame.");
            Assert.AreEqual(game1Score, _state.GameScores["p0"], "GameScores should be preserved after StartNextGame.");

            // Simulate Game 2: reassign roles (setup happens via SetupState transition).
            // Manually set WinResult for next GameOver.
            _state.WinResult = new WinConditionResult(true, Role.Agent, "Test2");

            var gameOver2 = new GameOverState();
            gameOver2.OnEnter(_context);

            int game2Cumulative = _state.GameScores.GetValueOrDefault("p0", 0);
            Assert.IsTrue(game2Cumulative > game1Score, "Cumulative scores should increase across games.");
        }
    }
}
