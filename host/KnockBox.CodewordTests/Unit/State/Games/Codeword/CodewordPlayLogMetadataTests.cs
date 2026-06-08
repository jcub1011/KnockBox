using KnockBox.Codeword;
using KnockBox.Codeword.Services.State.Games;
using KnockBox.Codeword.Services.State.Games.Data;
using KnockBox.Codeword.Services.State.PlayLog;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnockBox.Codeword.Tests.Unit.State.Games.Codeword
{
    [TestClass]
    public class CodewordPlayLogMetadataTests
    {
        private CodewordGameState _state = default!;
        private Guid _p0Id;
        private Guid _p1Id;
        private Guid _p2Id;

        [TestInitialize]
        public void Setup()
        {
            _p0Id = Guid.NewGuid();
            _p1Id = Guid.NewGuid();
            _p2Id = Guid.NewGuid();

            var host = UserFactory.Create("Host", Guid.NewGuid());
            _state = new CodewordGameState(host, NullLogger<CodewordGameState>.Instance);

            // Seed a match-over state: final game reached, scores accumulated, Agents won.
            _state.UpdateSettings(s => s with { TotalGames = 3 });
            _state.CurrentGameNumber = 3;
            _state.SetPhase(CodewordGamePhase.GameOver);
            _state.WinResult = new WinConditionResult(GameOver: true, WinningTeam: Role.Agent, Reason: "All insiders found.");

            AddPlayer(_p0Id, "Player 0", Role.Agent, score: 5);
            AddPlayer(_p1Id, "Player 1", Role.Insider, score: 12);
            AddPlayer(_p2Id, "Player 2", Role.Agent, score: 8);
        }

        private void AddPlayer(Guid id, string name, Role role, int score)
        {
            _state.GamePlayers[id] = new CodewordPlayerState
            {
                PlayerId = id,
                DisplayName = name,
                Role = role,
            };
            _state.GameScores[id] = score;
        }

        [TestMethod]
        public void Build_IncludesMatchLevelMetadata()
        {
            var metadata = CodewordPlayLogMetadata.Build(_state, _p0Id);

            Assert.AreEqual("3", metadata["Games Played"]);
            Assert.AreEqual("3", metadata["Players"]);
            Assert.AreEqual("Agents", metadata["Outcome"]);
        }

        [TestMethod]
        public void Build_IncludesPersonalMetadata_WhenUserIsParticipant()
        {
            // p0 has 5 pts; p1 (12) and p2 (8) are both ahead → placement 3 / 3.
            var metadata = CodewordPlayLogMetadata.Build(_state, _p0Id);

            Assert.AreEqual("5", metadata["My Score"]);
            Assert.AreEqual("3 / 3", metadata["Placement"]);
            Assert.AreEqual("Agent", metadata["My Role"]);
        }

        [TestMethod]
        public void Build_RanksTopScorerFirst()
        {
            // p1 is the highest scorer → placement 1 / 3.
            var metadata = CodewordPlayLogMetadata.Build(_state, _p1Id);

            Assert.AreEqual("12", metadata["My Score"]);
            Assert.AreEqual("1 / 3", metadata["Placement"]);
            Assert.AreEqual("Insider", metadata["My Role"]);
        }

        [TestMethod]
        public void Build_OmitsPersonalMetadata_WhenUserIsNotParticipant()
        {
            var metadata = CodewordPlayLogMetadata.Build(_state, Guid.NewGuid());

            Assert.IsFalse(metadata.ContainsKey("My Score"));
            Assert.IsFalse(metadata.ContainsKey("Placement"));
            Assert.IsFalse(metadata.ContainsKey("My Role"));
            // Match-level keys are still present for a spectating host.
            Assert.AreEqual("Agents", metadata["Outcome"]);
        }

        [TestMethod]
        public void Build_OmitsPersonalMetadata_WhenUserIdIsNull()
        {
            var metadata = CodewordPlayLogMetadata.Build(_state, currentUserId: null);

            Assert.IsFalse(metadata.ContainsKey("My Score"));
            Assert.AreEqual("3", metadata["Games Played"]);
        }
    }
}
