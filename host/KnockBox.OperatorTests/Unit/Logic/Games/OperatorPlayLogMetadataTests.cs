using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.Games;
using KnockBox.Operator.Services.State;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnockBox.Operator.Tests.Unit.Logic.Games
{
    [TestClass]
    public class OperatorPlayLogMetadataTests
    {
        private OperatorGameState _state = default!;

        [TestInitialize]
        public void Setup()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            _state = new OperatorGameState(host, NullLogger<OperatorGameState>.Instance);
            // Register players while the lobby is joinable (the default GameOver phase
            // the page checks does not gate registration; the helper never reads Phase).
            _state.Execute(() => _state.SetJoinable(true));
            _state.TurnCount = 12;
        }

        // Registers the player on the roster (so DisplayName resolves via Participants)
        // and seeds their per-player game state. Returns the player's user id.
        private Guid AddPlayer(string name, decimal points, DateTimeOffset? scoreTimestamp = null)
        {
            var user = UserFactory.Create(name, Guid.NewGuid());
            var reg = _state.RegisterPlayer(user);
            Assert.IsTrue(reg.TryGetSuccess(out _), $"Failed to register player [{name}].");

            _state.GamePlayers[user.Id] = new OperatorPlayerState
            {
                UserId = user.Id,
                CurrentPoints = points,
                ScoreTimestamp = scoreTimestamp ?? DateTimeOffset.UtcNow,
            };
            return user.Id;
        }

        [TestMethod]
        public void Build_RanksClosestToZero_AndReportsMatchLevelKeys()
        {
            AddPlayer("Far", -300m);
            var nearId = AddPlayer("Near", 5m);
            AddPlayer("Mid", 200m);
            _state.WinnerPlayerId = nearId;

            var metadata = OperatorPlayLogMetadata.Build(_state, currentUserId: null);

            Assert.AreEqual("3", metadata["Players"]);
            Assert.AreEqual("12", metadata["Rounds"]);
            Assert.AreEqual("Near", metadata["Winner"]);
            // No current user → no personal keys.
            Assert.IsFalse(metadata.ContainsKey("My Points"));
            Assert.IsFalse(metadata.ContainsKey("Placement"));
        }

        [TestMethod]
        public void Build_CurrentUserIsPlayer_IncludesPersonalKeys()
        {
            var winnerId = AddPlayer("Winner", 3m);   // closest to zero → 1st
            var meId = AddPlayer("Me", -150m);         // 2nd
            AddPlayer("Last", 500m);                    // 3rd
            _state.WinnerPlayerId = winnerId;

            var metadata = OperatorPlayLogMetadata.Build(_state, meId);

            Assert.AreEqual("2 / 3", metadata["Placement"]);
            Assert.AreEqual("-150", metadata["My Points"]);
            Assert.AreEqual("Winner", metadata["Winner"]);
        }

        [TestMethod]
        public void Build_CurrentUserNotAPlayer_OmitsPersonalKeys()
        {
            var winnerId = AddPlayer("A", 10m);
            AddPlayer("B", 20m);
            _state.WinnerPlayerId = winnerId;

            var metadata = OperatorPlayLogMetadata.Build(_state, Guid.NewGuid());

            Assert.IsFalse(metadata.ContainsKey("My Points"));
            Assert.IsFalse(metadata.ContainsKey("Placement"));
            Assert.AreEqual("2", metadata["Players"]);
        }

        [TestMethod]
        public void Build_TieBrokenByEarlierScoreTimestamp()
        {
            var early = DateTimeOffset.UtcNow.AddMinutes(-5);
            var late = DateTimeOffset.UtcNow;
            var earlyId = AddPlayer("Early", 10m, early);
            var lateId = AddPlayer("Late", -10m, late);
            _state.WinnerPlayerId = earlyId;

            // Equal magnitude (10) → earlier ScoreTimestamp ranks first.
            var earlyMeta = OperatorPlayLogMetadata.Build(_state, earlyId);
            var lateMeta = OperatorPlayLogMetadata.Build(_state, lateId);

            Assert.AreEqual("1 / 2", earlyMeta["Placement"]);
            Assert.AreEqual("2 / 2", lateMeta["Placement"]);
        }
    }
}
