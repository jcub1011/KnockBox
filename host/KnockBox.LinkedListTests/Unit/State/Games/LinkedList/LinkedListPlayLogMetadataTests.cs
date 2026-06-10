using KnockBox.Core.Services.State.Users;
using KnockBox.LinkedList.Services.State.Games;
using KnockBox.LinkedList.Services.State.Games.PlayLog;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.LinkedList.Tests.Unit.State
{
    [TestClass]
    public class LinkedListPlayLogMetadataTests
    {
        private Mock<ILogger<LinkedListGameState>> _loggerMock = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            _loggerMock = new Mock<ILogger<LinkedListGameState>>();
            _host = UserFactory.Create("HostUser", Guid.NewGuid());
        }

        /// <summary>Seeds a finished (GameOver) cooperative match with a single chain, two
        /// players, and one superlative awarded to the local player.</summary>
        private LinkedListGameState SeedGameOver(Guid meId, Guid otherId)
        {
            var state = new LinkedListGameState(_host, _loggerMock.Object);

            state.Execute(() =>
            {
                state.SetPhase(LinkedListGamePhase.GameOver);
                state.RoundNumber = 3;

                var group = new ChainState { GroupId = "g", GroupName = "Everyone" };
                group.Chain.Add(new ChainLink("alpha", "beta", meId, "Me", false));
                group.Chain.Add(new ChainLink("beta", "gamma", otherId, "Other", false));
                group.DestinationReached = true;
                state.Groups.Add(group);

                state.GamePlayers[meId] = new LinkedListPlayerState
                {
                    PlayerId = meId,
                    DisplayName = "Me",
                    AcceptedPairs = 2,
                    RejectionsReceived = 1,
                };
                state.GamePlayers[otherId] = new LinkedListPlayerState
                {
                    PlayerId = otherId,
                    DisplayName = "Other",
                    AcceptedPairs = 1,
                    RejectionsReceived = 0,
                };

                state.Superlatives =
                [
                    new Superlative("Loop Lord", "🔁", meId, "Me", "Most loop pairs"),
                ];
            });

            return state;
        }

        [TestMethod]
        public void Build_WhenLocalUserPlayed_EmitsTeamAndPersonalKeys()
        {
            var meId = Guid.NewGuid();
            var otherId = Guid.NewGuid();
            using var state = SeedGameOver(meId, otherId);

            var metadata = LinkedListPlayLogMetadata.Build(state, meId);

            // Team-level keys.
            Assert.AreEqual("2", metadata["Chain Length"]);
            Assert.AreEqual("Yes", metadata["Destination Reached"]);
            Assert.AreEqual("3", metadata["Rounds"]);
            Assert.AreEqual("2", metadata["Players"]);

            // Personal keys.
            Assert.AreEqual("2", metadata["My Accepted Pairs"]);
            Assert.AreEqual("1", metadata["Rejections Received"]);
            Assert.AreEqual("Loop Lord", metadata["Superlatives"]);
        }

        [TestMethod]
        public void Build_WhenLocalUserDidNotPlay_OmitsPersonalKeys()
        {
            var meId = Guid.NewGuid();
            var otherId = Guid.NewGuid();
            using var state = SeedGameOver(meId, otherId);

            // A spectator/host id that is not in GamePlayers.
            var metadata = LinkedListPlayLogMetadata.Build(state, Guid.NewGuid());

            Assert.AreEqual("2", metadata["Chain Length"]);
            Assert.AreEqual("Yes", metadata["Destination Reached"]);
            Assert.IsFalse(metadata.ContainsKey("My Accepted Pairs"));
            Assert.IsFalse(metadata.ContainsKey("Rejections Received"));
            Assert.IsFalse(metadata.ContainsKey("Superlatives"));
        }
    }
}
