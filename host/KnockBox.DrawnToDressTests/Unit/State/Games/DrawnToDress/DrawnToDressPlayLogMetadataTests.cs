using KnockBox.DrawnToDress.Services.State.Games;
using KnockBox.DrawnToDress.Services.State.Games.Data;
using KnockBox.DrawnToDress.Services.State.Games.PlayLog;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.DrawnToDress.Tests.Unit.State.Games.DrawnToDress
{
    /// <summary>
    /// Verifies <see cref="DrawnToDressPlayLogMetadata.Build"/> maps the terminal
    /// <see cref="DrawnToDressGameState.Leaderboard"/> into the expected match-level and
    /// personal metadata pairs.
    /// </summary>
    [TestClass]
    public class DrawnToDressPlayLogMetadataTests
    {
        private static readonly Guid PlayerA = Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA");
        private static readonly Guid PlayerB = Guid.Parse("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB");

        private static DrawnToDressGameState CreateFinalState()
        {
            var host = UserFactory.Create("Host", Guid.Parse("00000000-0000-0000-0000-000000000001"));
            var state = new DrawnToDressGameState(host, new Mock<ILogger<DrawnToDressGameState>>().Object);

            // Two-round Swiss tournament played out.
            state.VotingRounds =
            [
                new VotingRound { RoundNumber = 1 },
                new VotingRound { RoundNumber = 2 },
            ];

            // Leaderboard is stored in final rank order: A first (winner), B second.
            state.Leaderboard =
            [
                new LeaderboardEntry { PlayerId = PlayerA, DisplayName = "Player A", TotalScore = 12.5, Rank = 1 },
                new LeaderboardEntry { PlayerId = PlayerB, DisplayName = "Player B", TotalScore = 7, Rank = 2 },
            ];

            return state;
        }

        [TestMethod]
        public void Build_ForParticipant_EmitsMatchAndPersonalKeys()
        {
            var state = CreateFinalState();

            var metadata = DrawnToDressPlayLogMetadata.Build(state, PlayerB);

            // Match-level keys (always present).
            Assert.AreEqual("Player A", metadata["Winner"]);
            Assert.AreEqual("2", metadata["Players"]);
            Assert.AreEqual("2", metadata["Rounds"]);

            // Personal keys for the participating local user (second place).
            Assert.AreEqual("2 / 2", metadata["Placement"]);
            Assert.AreEqual("7", metadata["Score"]);
        }

        [TestMethod]
        public void Build_ForNonParticipant_OmitsPersonalKeys()
        {
            var state = CreateFinalState();

            var metadata = DrawnToDressPlayLogMetadata.Build(state, Guid.NewGuid());

            Assert.AreEqual("Player A", metadata["Winner"]);
            Assert.AreEqual("2", metadata["Players"]);
            Assert.AreEqual("2", metadata["Rounds"]);
            Assert.IsFalse(metadata.ContainsKey("Placement"));
            Assert.IsFalse(metadata.ContainsKey("Score"));
        }

        [TestMethod]
        public void Build_WithEmptyLeaderboard_ReturnsEmpty()
        {
            var host = UserFactory.Create("Host", Guid.Parse("00000000-0000-0000-0000-000000000001"));
            var state = new DrawnToDressGameState(host, new Mock<ILogger<DrawnToDressGameState>>().Object);

            var metadata = DrawnToDressPlayLogMetadata.Build(state, PlayerA);

            Assert.AreEqual(0, metadata.Count);
        }
    }
}
