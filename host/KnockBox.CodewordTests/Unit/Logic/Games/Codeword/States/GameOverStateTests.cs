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
    public class GameOverStateTests
    {
        private Mock<IRandomNumberService> _rng = default!;
        private Mock<ILogger> _logger = default!;
        private Mock<ILogger<CodewordGameState>> _stateLogger = default!;
        private CodewordGameState _state = default!;
        private CodewordGameContext _context = default!;

        private Guid _hostId = default!;
        private Guid _p0Id = default!;
        private Guid _p1Id = default!;
        private Guid _p2Id = default!;

        [TestInitialize]
        public void Setup()
        {
            _hostId = Guid.NewGuid();
            _p0Id = Guid.NewGuid();
            _p1Id = Guid.NewGuid();
            _p2Id = Guid.NewGuid();

            _rng = new Mock<IRandomNumberService>();
            _rng.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<RandomType>()))
                .Returns(0);
            _rng.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<RandomType>()))
                .Returns(0);
            _logger = new Mock<ILogger>();
            _stateLogger = new Mock<ILogger<CodewordGameState>>();

            var host = UserFactory.Create("Host", _hostId);
            _state = new CodewordGameState(host, _stateLogger.Object);
            _context = new CodewordGameContext(_state, _rng.Object, _logger.Object);

            AddPlayer(_p0Id, "Player 0", Role.Agent, "Ocean");
            AddPlayer(_p1Id, "Player 1", Role.Agent, "Ocean");
            AddPlayer(_p2Id, "Player 2", Role.Insider, "Lake");
            _state.CurrentWordPair = ["Ocean", "Lake"];
        }

        private void AddPlayer(Guid id, string name, Role role, string? secretWord)
        {
            _state.GamePlayers[id] = new CodewordPlayerState
            {
                PlayerId = id,
                DisplayName = name,
                Role = role,
                SecretWord = secretWord
            };
            _state.TurnManager.TurnOrder.Add(id);
        }

        [TestMethod]
        public void OnEnter_SetsPhaseToGameOver()
        {
            _state.WinResult = new WinConditionResult(true, Role.Agent, "Test");

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);

            Assert.AreEqual(CodewordGamePhase.GameOver, _state.Phase);
        }

        [TestMethod]
        public void OnEnter_AppliesEndOfGameScoring()
        {
            _state.WinResult = new WinConditionResult(true, Role.Agent, "Test");

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);

            // p0: Agent, alive → +2 survive + +1 winning team = 3.
            Assert.AreEqual(3, _state.GamePlayers[_p0Id].Score);
        }

        [TestMethod]
        public void OnEnter_AccumulatesScoresToGameScores()
        {
            _state.WinResult = new WinConditionResult(true, Role.Agent, "Test");

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);

            Assert.IsTrue(_state.GameScores.ContainsKey(_p0Id));
            Assert.IsGreaterThan(0, _state.GameScores[_p0Id]);
        }

        [TestMethod]
        public void HandleCommand_StartNextGame_HostOnly()
        {
            _state.UpdateSettings(s => s with { TotalGames = 5 });
            _state.WinResult = new WinConditionResult(true, Role.Agent, "Test");

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);

            var result = gameOver.HandleCommand(_context, new StartNextGameCommand(_hostId));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsInstanceOfType<SetupState>(result.Value);
        }

        [TestMethod]
        public void HandleCommand_StartNextGame_RejectsNonHost()
        {
            _state.UpdateSettings(s => s with { TotalGames = 5 });
            _state.WinResult = new WinConditionResult(true, Role.Agent, "Test");

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);

            var result = gameOver.HandleCommand(_context, new StartNextGameCommand(_p0Id));
            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void HandleCommand_StartNextGame_IncrementsGameNumber()
        {
            _state.UpdateSettings(s => s with { TotalGames = 5 });
            _state.CurrentGameNumber = 1;
            _state.WinResult = new WinConditionResult(true, Role.Agent, "Test");

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);

            gameOver.HandleCommand(_context, new StartNextGameCommand(_hostId));
            Assert.AreEqual(2, _state.CurrentGameNumber);
        }

        [TestMethod]
        public void HandleCommand_StartNextGame_ClearsPlayerState()
        {
            _state.UpdateSettings(s => s with { TotalGames = 5 });
            _state.WinResult = new WinConditionResult(true, Role.Agent, "Test");

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);
            gameOver.HandleCommand(_context, new StartNextGameCommand(_hostId));

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
            _state.UpdateSettings(s => s with { TotalGames = 5 });
            _state.UsedClues["test"] = "SomePlayer";
            _state.CurrentRoundClues.Add(new ClueEntry(_p0Id, "P0", "test"));
            _state.CurrentRoundVotes.Add(new VoteEntry(_p0Id, "P0", _p1Id, "P1"));
            _state.WinResult = new WinConditionResult(true, Role.Agent, "Test");

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);
            gameOver.HandleCommand(_context, new StartNextGameCommand(_hostId));

            Assert.AreEqual(0, _state.CurrentEliminationCycle);
            Assert.AreEqual(0, _state.TurnManager.CurrentPlayerIndex);
            Assert.IsNull(_state.CurrentWordPair);
            Assert.IsEmpty(_state.CurrentRoundClues);
            Assert.IsEmpty(_state.CurrentRoundVotes);
            Assert.IsEmpty(_state.UsedClues);
            Assert.IsNull(_state.LastElimination);
            Assert.IsNull(_state.LastInformantGuess);
            Assert.IsFalse(_state.AwaitingInformantGuess);
            Assert.IsNull(_state.WinResult);
        }

        [TestMethod]
        public void HandleCommand_StartNextGame_PreservesUsedWordPairIndices()
        {
            _state.UpdateSettings(s => s with { TotalGames = 5 });
            _context.UsedWordPairIndices.Add(0);
            _context.UsedWordPairIndices.Add(1);
            _state.WinResult = new WinConditionResult(true, Role.Agent, "Test");

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);
            gameOver.HandleCommand(_context, new StartNextGameCommand(_hostId));

            Assert.HasCount(2, _context.UsedWordPairIndices);
        }

        [TestMethod]
        public void HandleCommand_StartNextGame_PreservesGameScores()
        {
            _state.UpdateSettings(s => s with { TotalGames = 5 });
            _state.WinResult = new WinConditionResult(true, Role.Agent, "Test");

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);

            // Scores accumulated in OnEnter.
            int p0Score = _state.GameScores.GetValueOrDefault(_p0Id, 0);

            gameOver.HandleCommand(_context, new StartNextGameCommand(_hostId));

            Assert.AreEqual(p0Score, _state.GameScores[_p0Id]);
        }

        [TestMethod]
        public void HandleCommand_StartNextGame_RejectsWhenAllGamesPlayed()
        {
            _state.UpdateSettings(s => s with { TotalGames = 1 });
            _state.CurrentGameNumber = 1;
            _state.WinResult = new WinConditionResult(true, Role.Agent, "Test");

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);

            var result = gameOver.HandleCommand(_context, new StartNextGameCommand(_hostId));
            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void HandleCommand_ReturnToLobby_HostOnly()
        {
            _state.WinResult = new WinConditionResult(true, Role.Agent, "Test");

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);

            var result = gameOver.HandleCommand(_context, new ReturnToLobbyCommand(_hostId));
            Assert.IsTrue(result.IsSuccess);
            Assert.IsNull(result.Value); // Null signals lobby transition.
        }

        [TestMethod]
        public void HandleCommand_ReturnToLobby_RejectsNonHost()
        {
            _state.WinResult = new WinConditionResult(true, Role.Agent, "Test");

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);

            var result = gameOver.HandleCommand(_context, new ReturnToLobbyCommand(_p0Id));
            Assert.IsFalse(result.IsSuccess);
        }

        [TestMethod]
        public void OnEnter_EvaluatesWinConditionsIfNotSet()
        {
            // WinResult is null; GameOverState should evaluate it.
            _state.WinResult = null;
            // With only 2 alive players (eliminate p2), the game ends.
            _state.GamePlayers[_p2Id].IsEliminated = true;

            var gameOver = new GameOverState();
            gameOver.OnEnter(_context);

            Assert.IsNotNull(_state.WinResult);
            Assert.IsTrue(_state.WinResult.GameOver);
        }

        [TestMethod]
        public void MultiGame_CumulativeScoresTrackedCorrectly()
        {
            _state.UpdateSettings(s => s with { TotalGames = 3 });
            _state.WinResult = new WinConditionResult(true, Role.Agent, "Test");

            // Game 1: Apply scoring.
            var gameOver1 = new GameOverState();
            gameOver1.OnEnter(_context);

            // p0: Agent, alive → +2 + +1 = 3. GameScores[_p0Id] = 3.
            int game1Score = _state.GameScores.GetValueOrDefault(_p0Id, 0);
            Assert.IsGreaterThan(0, game1Score, "Game 1 score should be > 0.");

            // Start next game → resets player Score but preserves GameScores.
            gameOver1.HandleCommand(_context, new StartNextGameCommand(_hostId));
            Assert.AreEqual(0, _state.GamePlayers[_p0Id].Score, "Player score should reset after StartNextGame.");
            Assert.AreEqual(game1Score, _state.GameScores[_p0Id], "GameScores should be preserved after StartNextGame.");

            // Simulate Game 2: reassign roles (setup happens via SetupState transition).
            // Manually set WinResult for next GameOver.
            _state.WinResult = new WinConditionResult(true, Role.Agent, "Test2");

            var gameOver2 = new GameOverState();
            gameOver2.OnEnter(_context);

            int game2Cumulative = _state.GameScores.GetValueOrDefault(_p0Id, 0);
            Assert.IsGreaterThan(game1Score, game2Cumulative, "Cumulative scores should increase across games.");
        }
    }
}
