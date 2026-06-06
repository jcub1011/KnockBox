using KnockBox.Codeword.Services.Logic.Games.FSM;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Codeword.Services.State.Games;
using KnockBox.Codeword.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.Codeword.Tests.Unit.Logic.Games.Codeword
{
    [TestClass]
    public class CodewordGameContextTests
    {
        private Mock<IRandomNumberService> _randomMock = default!;
        private Mock<ILogger> _loggerMock = default!;
        private Mock<ILogger<CodewordGameState>> _stateLoggerMock = default!;
        private CodewordGameState _state = default!;
        private CodewordGameContext _context = default!;

        // Stable player IDs used across tests
        private Guid _hostId = default!;
        private Guid _p1Id = default!;
        private Guid _p2Id = default!;
        private Guid _p3Id = default!;
        private Guid _p4Id = default!;

        [TestInitialize]
        public void Setup()
        {
            _hostId = Guid.NewGuid();
            _p1Id = Guid.NewGuid();
            _p2Id = Guid.NewGuid();
            _p3Id = Guid.NewGuid();
            _p4Id = Guid.NewGuid();

            _randomMock = new Mock<IRandomNumberService>();
            // Default: always return 0 for single-arg, and 0 for two-arg.
            _randomMock
                .Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<RandomType>()))
                .Returns(0);
            _randomMock
                .Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<RandomType>()))
                .Returns(0);
            _loggerMock = new Mock<ILogger>();
            _stateLoggerMock = new Mock<ILogger<CodewordGameState>>();

            var host = UserFactory.Create("Host", _hostId);
            _state = new CodewordGameState(host, _stateLoggerMock.Object);
            _context = new CodewordGameContext(_state, _randomMock.Object, _loggerMock.Object);
        }

        private CodewordPlayerState AddPlayer(Guid id, string name)
        {
            var ps = new CodewordPlayerState { PlayerId = id, DisplayName = name };
            _state.GamePlayers[id] = ps;
            return ps;
        }

        // ── GetRoleDistribution ───────────────────────────────────────────────

        [TestMethod]
        [DataRow(4, 3, 1, 0)]
        [DataRow(5, 3, 1, 1)]
        [DataRow(6, 4, 1, 1)]
        [DataRow(7, 4, 2, 1)]
        [DataRow(8, 5, 2, 1)]
        public void GetRoleDistribution_ReturnsCorrectCounts(
            int playerCount, int expectedAgents, int expectedInsiders, int expectedInformants)
        {
            var (agents, insiders, informants) = CodewordGameContext.GetRoleDistribution(playerCount);
            Assert.AreEqual(expectedAgents, agents);
            Assert.AreEqual(expectedInsiders, insiders);
            Assert.AreEqual(expectedInformants, informants);
        }

        [TestMethod]
        [DataRow(3)]
        [DataRow(9)]
        public void GetRoleDistribution_ThrowsForInvalidPlayerCount(int playerCount)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CodewordGameContext.GetRoleDistribution(playerCount));
        }

        // ── AssignRoles ───────────────────────────────────────────────────────

        [TestMethod]
        [DataRow(4)]
        [DataRow(5)]
        [DataRow(6)]
        [DataRow(7)]
        [DataRow(8)]
        public void AssignRoles_AssignsCorrectDistribution(int playerCount)
        {
            for (int i = 0; i < playerCount; i++)
                AddPlayer(Guid.NewGuid(), $"Player {i}");

            // Make the random return a sequence that just yields 1 for word index selection
            // to avoid infinite loop when picking 2 distinct word indices.
            int callCount = 0;
            _randomMock
                .Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<RandomType>()))
                .Returns((int max, RandomType _) =>
                {
                    callCount++;
                    // Return alternating 0, 1 to ensure distinct word indices.
                    return callCount % 2 == 0 ? 1 % max : 0;
                });

            _context.AssignRoles();

            var players = _state.GamePlayers.Values.ToList();
            var (expectedAgents, expectedInsiders, expectedInformants) =
                CodewordGameContext.GetRoleDistribution(playerCount);

            Assert.AreEqual(expectedAgents, players.Count(p => p.Role == Role.Agent));
            Assert.AreEqual(expectedInsiders, players.Count(p => p.Role == Role.Insider));
            Assert.AreEqual(expectedInformants, players.Count(p => p.Role == Role.Informant));

            // All Agents and Insiders should have secret words.
            Assert.IsTrue(players.Where(p => p.Role == Role.Agent).All(p => p.SecretWord is not null));
            Assert.IsTrue(players.Where(p => p.Role == Role.Insider).All(p => p.SecretWord is not null));

            // Informants should have no secret word.
            Assert.IsTrue(players.Where(p => p.Role == Role.Informant).All(p => p.SecretWord is null));

            // Agent and Insider words should be different.
            var agentWords = players.Where(p => p.Role == Role.Agent).Select(p => p.SecretWord).Distinct().ToList();
            var insiderWords = players.Where(p => p.Role == Role.Insider).Select(p => p.SecretWord).Distinct().ToList();
            Assert.HasCount(1, agentWords, "All agents should share the same word.");
            if (insiderWords.Count > 0)
            {
                Assert.HasCount(1, insiderWords, "All insiders should share the same word.");
                Assert.AreNotEqual(agentWords[0], insiderWords[0], "Agent and Insider words must differ.");
            }

            Assert.IsNotNull(_state.CurrentWordPair);
            Assert.HasCount(2, _state.CurrentWordPair);
        }

        // ── SelectWordPair ────────────────────────────────────────────────────

        [TestMethod]
        public void SelectWordPair_ReturnsDistinctWords()
        {
            // Ensure word indices differ: first call returns 0, second returns 1.
            int callCount = 0;
            _randomMock
                .Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<RandomType>()))
                .Returns((int max, RandomType _) =>
                {
                    callCount++;
                    return callCount % 2 == 0 ? 1 % max : 0;
                });

            var (agentWord, insiderWord) = _context.SelectWordPair();

            Assert.IsFalse(string.IsNullOrWhiteSpace(agentWord));
            Assert.IsFalse(string.IsNullOrWhiteSpace(insiderWord));
            Assert.AreNotEqual(agentWord, insiderWord);
        }

        [TestMethod]
        public void SelectWordPair_TracksUsedIndices()
        {
            int callCount = 0;
            _randomMock
                .Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<RandomType>()))
                .Returns((int max, RandomType _) =>
                {
                    callCount++;
                    return callCount % 2 == 0 ? 1 % max : 0;
                });

            _context.SelectWordPair();
            Assert.HasCount(1, _context.UsedWordPairIndices);
        }

        [TestMethod]
        public void SelectWordPair_ResetsWhenAllUsed()
        {
            // Use a small word bank for this test.
            _context.UseWordBank([new WordGroup(["A", "B"]), new WordGroup(["C", "D"])]);

            int callCount = 0;
            _randomMock
                .Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<RandomType>()))
                .Returns((int max, RandomType _) =>
                {
                    callCount++;
                    return callCount % 2 == 0 ? 1 % max : 0;
                });

            // Use all available groups.
            _context.SelectWordPair();
            _context.SelectWordPair();
            Assert.HasCount(2, _context.UsedWordPairIndices);

            // Third call should reset and succeed.
            _context.SelectWordPair();
            Assert.IsGreaterThanOrEqualTo(1, _context.UsedWordPairIndices.Count);
        }

        // ── GetAlivePlayers / GetAlivePlayerCount ─────────────────────────────

        [TestMethod]
        public void GetAlivePlayers_ReturnsOnlyNonEliminatedPlayers()
        {
            var p1 = AddPlayer(_p1Id, "P1");
            var p2 = AddPlayer(_p2Id, "P2");
            var p3 = AddPlayer(_p3Id, "P3");
            p2.IsEliminated = true;

            var alive = _context.GetAlivePlayers();
            Assert.HasCount(2, alive);
            Assert.Contains(p => p.PlayerId == _p1Id, alive);
            Assert.Contains(p => p.PlayerId == _p3Id, alive);
        }

        [TestMethod]
        public void GetAlivePlayerCount_ReturnsCorrectCount()
        {
            AddPlayer(_p1Id, "P1");
            var p2 = AddPlayer(_p2Id, "P2");
            AddPlayer(_p3Id, "P3");
            p2.IsEliminated = true;

            Assert.AreEqual(2, _context.GetAlivePlayerCount());
        }

        // ── TallyVotes ────────────────────────────────────────────────────────

        [TestMethod]
        public void TallyVotes_ReturnsMajorityTarget()
        {
            var p1 = AddPlayer(_p1Id, "P1");
            var p2 = AddPlayer(_p2Id, "P2");
            var p3 = AddPlayer(_p3Id, "P3");

            p1.HasVoted = true; p1.VoteTargetId = _p3Id;
            p2.HasVoted = true; p2.VoteTargetId = _p3Id;
            p3.HasVoted = true; p3.VoteTargetId = _p1Id;

            var result = _context.TallyVotes();
            Assert.AreEqual(_p3Id, result);
        }

        [TestMethod]
        public void TallyVotes_ReturnsNullOnTie()
        {
            var p1 = AddPlayer(_p1Id, "P1");
            var p2 = AddPlayer(_p2Id, "P2");
            var p3 = AddPlayer(_p3Id, "P3");
            var p4 = AddPlayer(_p4Id, "P4");

            p1.HasVoted = true; p1.VoteTargetId = _p3Id;
            p2.HasVoted = true; p2.VoteTargetId = _p4Id;
            p3.HasVoted = true; p3.VoteTargetId = _p1Id;
            p4.HasVoted = true; p4.VoteTargetId = _p2Id;

            var result = _context.TallyVotes();
            Assert.IsNull(result);
        }

        [TestMethod]
        public void TallyVotes_ReturnsNullWhenNoVotes()
        {
            AddPlayer(_p1Id, "P1");
            AddPlayer(_p2Id, "P2");

            var result = _context.TallyVotes();
            Assert.IsNull(result);
        }

        [TestMethod]
        public void TallyVotes_IgnoresEliminatedPlayers()
        {
            var p1 = AddPlayer(_p1Id, "P1");
            var p2 = AddPlayer(_p2Id, "P2");
            var p3 = AddPlayer(_p3Id, "P3");
            p1.IsEliminated = true;
            p1.HasVoted = true; p1.VoteTargetId = _p2Id;
            p2.HasVoted = true; p2.VoteTargetId = _p3Id;
            p3.HasVoted = true; p3.VoteTargetId = _p2Id;

            var result = _context.TallyVotes();
            // Only p2 and p3 are alive. p2 voted for p3, p3 voted for p2 = tie.
            Assert.IsNull(result);
        }

        // ── CheckWinConditions ────────────────────────────────────────────────

        [TestMethod]
        public void CheckWinConditions_GameContinues_WhenMoreThanTwoPlayersAndNoVote()
        {
            AddPlayer(_p1Id, "P1").Role = Role.Agent;
            AddPlayer(_p2Id, "P2").Role = Role.Agent;
            AddPlayer(_p3Id, "P3").Role = Role.Insider;

            var result = _context.CheckWinConditions();
            Assert.IsFalse(result.GameOver);
        }

        [TestMethod]
        public void CheckWinConditions_EndsWhenTwoOrFewerAlive()
        {
            AddPlayer(_p1Id, "P1").Role = Role.Agent;
            AddPlayer(_p2Id, "P2").Role = Role.Insider;
            var p3 = AddPlayer(_p3Id, "P3");
            p3.Role = Role.Agent;
            p3.IsEliminated = true;

            var result = _context.CheckWinConditions();
            Assert.IsTrue(result.GameOver);
        }

        [TestMethod]
        public void CheckWinConditions_InformantWins_WhenAlive()
        {
            AddPlayer(_p1Id, "P1").Role = Role.Agent;
            AddPlayer(_p2Id, "P2").Role = Role.Informant;
            var p3 = AddPlayer(_p3Id, "P3");
            p3.Role = Role.Agent;
            p3.IsEliminated = true;

            var result = _context.CheckWinConditions();
            Assert.IsTrue(result.GameOver);
            Assert.AreEqual(Role.Informant, result.WinningTeam);
        }

        [TestMethod]
        public void CheckWinConditions_InsiderWins_WhenAliveAndNoInformant()
        {
            AddPlayer(_p1Id, "P1").Role = Role.Agent;
            AddPlayer(_p2Id, "P2").Role = Role.Insider;
            var p3 = AddPlayer(_p3Id, "P3");
            p3.Role = Role.Informant;
            p3.IsEliminated = true;

            var result = _context.CheckWinConditions();
            Assert.IsTrue(result.GameOver);
            Assert.AreEqual(Role.Insider, result.WinningTeam);
        }

        [TestMethod]
        public void CheckWinConditions_AgentsWin_WhenNoInformantOrInsiderAlive()
        {
            AddPlayer(_p1Id, "P1").Role = Role.Agent;
            AddPlayer(_p2Id, "P2").Role = Role.Agent;
            var p3 = AddPlayer(_p3Id, "P3");
            p3.Role = Role.Insider;
            p3.IsEliminated = true;
            var p4 = AddPlayer(_p4Id, "P4");
            p4.Role = Role.Informant;
            p4.IsEliminated = true;

            var result = _context.CheckWinConditions();
            Assert.IsTrue(result.GameOver);
            Assert.AreEqual(Role.Agent, result.WinningTeam);
        }

        [TestMethod]
        public void CheckWinConditions_DoesNotAutoEnd_WhenInsidersEliminated()
        {
            // Even though all Insiders are eliminated, game should continue if >2 alive.
            AddPlayer(_p1Id, "P1").Role = Role.Agent;
            AddPlayer(_p2Id, "P2").Role = Role.Agent;
            AddPlayer(_p3Id, "P3").Role = Role.Agent;
            var p4 = AddPlayer(_p4Id, "P4");
            p4.Role = Role.Insider;
            p4.IsEliminated = true;

            var result = _context.CheckWinConditions();
            Assert.IsFalse(result.GameOver, "Game should NOT auto-end when Insiders are eliminated.");
        }

        [TestMethod]
        public void CheckWinConditions_EndsOnMajorityVote()
        {
            AddPlayer(_p1Id, "P1").Role = Role.Agent;
            AddPlayer(_p2Id, "P2").Role = Role.Agent;
            AddPlayer(_p3Id, "P3").Role = Role.Insider;
            AddPlayer(_p4Id, "P4").Role = Role.Agent;

            // Set up majority vote: 3 out of 4 voted to end (required = 3).
            _state.EndGameVoteStatus = new EndGameVoteStatus(
                [_p1Id, _p2Id, _p3Id], 3);

            var result = _context.CheckWinConditions();
            Assert.IsTrue(result.GameOver);
        }

        // ── ResetEliminationCycleState ────────────────────────────────────────

        [TestMethod]
        public void ResetEliminationCycleState_ClearsPerCycleData()
        {
            var p1 = AddPlayer(_p1Id, "P1");
            p1.HasSubmittedClue = true;
            p1.CurrentClue = "test";
            p1.VoteTargetId = _p2Id;
            p1.HasVoted = true;
            p1.HasVotedToEndGame = true;

            var p2 = AddPlayer(_p2Id, "P2");
            p2.IsEliminated = true; // Eliminated player should NOT be reset.

            _state.CurrentRoundClues.Add(new ClueEntry(_p1Id, "P1", "test"));
            _state.CurrentRoundVotes.Add(new VoteEntry(_p1Id, "P1", _p2Id, "P2"));
            _state.AwaitingInformantGuess = true;

            _context.ResetEliminationCycleState();

            Assert.IsFalse(p1.HasSubmittedClue);
            Assert.IsNull(p1.CurrentClue);
            Assert.IsNull(p1.VoteTargetId);
            Assert.IsFalse(p1.HasVoted);
            Assert.IsFalse(p1.HasVotedToEndGame);

            Assert.IsEmpty(_state.CurrentRoundClues);
            Assert.IsEmpty(_state.CurrentRoundVotes);
            Assert.IsFalse(_state.AwaitingInformantGuess);
        }

        // ── ApplyCycleScoring ─────────────────────────────────────────────────

        [TestMethod]
        public void ApplyCycleScoring_MinusOneForVotingForAgent()
        {
            var p1 = AddPlayer(_p1Id, "P1");
            p1.Role = Role.Agent;
            p1.HasVoted = true;
            p1.VoteTargetId = _p2Id;

            var p2 = AddPlayer(_p2Id, "P2");
            p2.Role = Role.Agent;

            _context.ApplyCycleScoring(null);

            Assert.AreEqual(-1, p1.Score);
        }

        [TestMethod]
        public void ApplyCycleScoring_NoChangeForVotingForNonAgent()
        {
            var p1 = AddPlayer(_p1Id, "P1");
            p1.Role = Role.Agent;
            p1.HasVoted = true;
            p1.VoteTargetId = _p2Id;

            var p2 = AddPlayer(_p2Id, "P2");
            p2.Role = Role.Insider;

            _context.ApplyCycleScoring(null);

            Assert.AreEqual(1, p1.Score);
        }

        // ── ApplyEndOfGameScoring ─────────────────────────────────────────────

        [TestMethod]
        public void ApplyEndOfGameScoring_Plus2ForSurvival()
        {
            var p1 = AddPlayer(_p1Id, "P1");
            p1.Role = Role.Agent;

            var p2 = AddPlayer(_p2Id, "P2");
            p2.Role = Role.Insider;
            p2.IsEliminated = true;

            var winResult = new WinConditionResult(true, Role.Agent, "Test");
            _context.ApplyEndOfGameScoring(winResult);

            Assert.AreEqual(3, p1.Score, "p1: +2 survived, +1 winning team");
            Assert.AreEqual(0, p2.Score, "p2: eliminated, not on winning team");
        }

        [TestMethod]
        public void ApplyEndOfGameScoring_Plus1ForWinningTeam()
        {
            var p1 = AddPlayer(_p1Id, "P1");
            p1.Role = Role.Agent;

            var p2 = AddPlayer(_p2Id, "P2");
            p2.Role = Role.Agent;
            p2.IsEliminated = true;

            var winResult = new WinConditionResult(true, Role.Agent, "Test");
            _context.ApplyEndOfGameScoring(winResult);

            // p1: +2 survived + +1 winning team = 3
            Assert.AreEqual(3, p1.Score);
            // p2: eliminated but on winning team = +1
            Assert.AreEqual(1, p2.Score);
        }

        [TestMethod]
        public void ApplyEndOfGameScoring_Plus3ForInformantCorrectGuess()
        {
            var p1 = AddPlayer(_p1Id, "P1");
            p1.Role = Role.Informant;
            p1.IsEliminated = true;

            _state.LastInformantGuess = new InformantGuessResult(_p1Id, "P1", "Ocean", true);

            var winResult = new WinConditionResult(true, Role.Agent, "Test");
            _context.ApplyEndOfGameScoring(winResult);

            // p1: eliminated, not on winning team (Agent won), but +3 for correct guess = 3
            Assert.AreEqual(3, p1.Score);
        }

        [TestMethod]
        public void ApplyEndOfGameScoring_PersistsToGameScores()
        {
            var p1 = AddPlayer(_p1Id, "P1");
            p1.Role = Role.Agent;
            p1.Score = 5; // Pre-existing score from cycles.

            var winResult = new WinConditionResult(true, Role.Agent, "Test");
            _context.ApplyEndOfGameScoring(winResult);

            // p1: +2 survived + +1 winning team = +3 added to existing 5 → Score = 8, GameScores = 8
            Assert.AreEqual(8, p1.Score);
            Assert.AreEqual(8, _state.GameScores[_p1Id]);
        }

        [TestMethod]
        public void ApplyEndOfGameScoring_AccumulatesAcrossMultipleGames()
        {
            var p1 = AddPlayer(_p1Id, "P1");
            p1.Role = Role.Agent;

            // Simulate Game 1 result persisted to GameScores.
            _state.GameScores[_p1Id] = 5;

            // Game 2: player scores +2 survived + +1 winning team.
            var winResult = new WinConditionResult(true, Role.Agent, "Test");
            _context.ApplyEndOfGameScoring(winResult);

            // Score this game = 3, cumulative GameScores = 5 + 3 = 8.
            Assert.AreEqual(3, p1.Score);
            Assert.AreEqual(8, _state.GameScores[_p1Id]);
        }

        // ── Command types ─────────────────────────────────────────────────────

        [TestMethod]
        public void AllCommandTypes_InheritFromBase()
        {
            var pId = Guid.NewGuid();
            var p2Id = Guid.NewGuid();
            CodewordCommand[] commands =
            [
                new SubmitClueCommand(pId, "clue"),
                new AdvanceToVoteCommand(pId),
                new VoteToEndGameCommand(pId),
                new CastVoteCommand(pId, p2Id),
                new InformantGuessCommand(pId, "word"),
                new StartNextGameCommand(pId),
                new ReturnToLobbyCommand(pId),
            ];

            foreach (var cmd in commands)
            {
                Assert.IsInstanceOfType<CodewordCommand>(cmd);
                Assert.AreEqual(pId, cmd.PlayerId);
            }
        }

        [TestMethod]
        public void SelectWordPair_WorksWithVariableSizeGroups()
        {
            // Provide groups with 2, 3, and 5 words.
            _context.UseWordBank([
                new WordGroup(["A", "B"]),
                new WordGroup(["C", "D", "E"]),
                new WordGroup(["F", "G", "H", "I", "J"]),
            ]);

            int callCount = 0;
            _randomMock
                .Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<RandomType>()))
                .Returns((int max, RandomType _) =>
                {
                    callCount++;
                    return callCount % 2 == 0 ? 1 % max : 0;
                });

            // All three groups should be selectable.
            for (int i = 0; i < 3; i++)
            {
                var (agentWord, insiderWord) = _context.SelectWordPair();
                Assert.IsFalse(string.IsNullOrWhiteSpace(agentWord));
                Assert.IsFalse(string.IsNullOrWhiteSpace(insiderWord));
                Assert.AreNotEqual(agentWord, insiderWord);
            }

            Assert.HasCount(3, _context.UsedWordPairIndices);
        }
    }
}
