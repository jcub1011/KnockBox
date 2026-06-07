using KnockBox.CardCounter.Services.Logic.Games;
using KnockBox.CardCounter.Services.State.Games;
using KnockBox.CardCounter.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.CardCounter.Tests.Unit.Logic.Games.CardCounter
{
    [TestClass]
    public class CardCounterPlayLogMetadataTests
    {
        private Mock<ILogger<CardCounterGameState>> _stateLoggerMock = default!;
        private CardCounterGameState _state = default!;

        [TestInitialize]
        public void Setup()
        {
            _stateLoggerMock = new Mock<ILogger<CardCounterGameState>>();
            var host = UserFactory.Create("Host", Guid.NewGuid());
            _state = new CardCounterGameState(host, _stateLoggerMock.Object);
            _state.SetPhase(GamePhase.GameOver);
        }

        private PlayerState AddPlayer(string name, double balance, Guid? id = null)
        {
            var playerId = id ?? Guid.NewGuid();
            var player = new PlayerState { PlayerId = playerId, DisplayName = name, Balance = balance };
            _state.GamePlayers[playerId] = player;
            return player;
        }

        [TestMethod]
        public void Build_ClosestToZero_RanksAscendingByMagnitude()
        {
            // Default win condition: closest to zero wins.
            AddPlayer("Far", -300);
            AddPlayer("Near", 50);
            AddPlayer("Mid", 200);

            var metadata = CardCounterPlayLogMetadata.Build(_state, currentUserId: null);

            Assert.AreEqual("3", metadata["Players"]);
            Assert.AreEqual("Closest to zero", metadata["Win Condition"]);
            Assert.AreEqual("Near", metadata["Winner"]);
            // No current user → no personal keys.
            Assert.IsFalse(metadata.ContainsKey("My Balance"));
            Assert.IsFalse(metadata.ContainsKey("Placement"));
        }

        [TestMethod]
        public void Build_FlipWinCondition_RanksDescendingByMagnitude()
        {
            _state.UpdateSettings(s => s with { FlipWinCondition = true });
            AddPlayer("Small", 50);
            AddPlayer("Big", -400);
            AddPlayer("Mid", 200);

            var metadata = CardCounterPlayLogMetadata.Build(_state, currentUserId: null);

            Assert.AreEqual("Highest magnitude", metadata["Win Condition"]);
            Assert.AreEqual("Big", metadata["Winner"]);
        }

        [TestMethod]
        public void Build_CurrentUserIsPlayer_IncludesPersonalKeys()
        {
            var meId = Guid.NewGuid();
            AddPlayer("Winner", 10);          // closest to zero → 1st
            AddPlayer("Me", -150, meId);      // 2nd
            AddPlayer("Last", 500);           // 3rd

            var metadata = CardCounterPlayLogMetadata.Build(_state, meId);

            Assert.AreEqual("2 / 3", metadata["Placement"]);
            Assert.AreEqual("-150", metadata["My Balance"]);
            Assert.AreEqual("Winner", metadata["Winner"]);
        }

        [TestMethod]
        public void Build_CurrentUserNotAPlayer_OmitsPersonalKeys()
        {
            AddPlayer("A", 10);
            AddPlayer("B", 20);

            var metadata = CardCounterPlayLogMetadata.Build(_state, Guid.NewGuid());

            Assert.IsFalse(metadata.ContainsKey("My Balance"));
            Assert.IsFalse(metadata.ContainsKey("Placement"));
            Assert.AreEqual("2", metadata["Players"]);
        }
    }
}
