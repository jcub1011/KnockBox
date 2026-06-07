using System;
using System.Collections.Generic;
using KnockBox.Core.Services.State.Users;
using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.HiddenAgenda.Services.State.Games.Data;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.HiddenAgenda.Tests.Unit.State
{
    [TestClass]
    public class HiddenAgendaPlayLogMetadataTests
    {
        private static readonly Guid WinnerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid LoserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        private HiddenAgendaGameState BuildMatchOverState()
        {
            var host = UserFactory.Create("Host", Guid.Parse("00000000-0000-0000-0000-000000000001"));
            var state = new HiddenAgendaGameState(host, new Mock<ILogger<HiddenAgendaGameState>>().Object);

            state.GamePlayers[LoserId] = new HiddenAgendaPlayerState
            {
                PlayerId = LoserId,
                DisplayName = "Alice",
                CumulativeScore = 20,
            };
            state.GamePlayers[WinnerId] = new HiddenAgendaPlayerState
            {
                PlayerId = WinnerId,
                DisplayName = "Bob",
                CumulativeScore = 25,
            };

            state.RoundResults.Add(new RoundResult(1, new Dictionary<Guid, PlayerRoundResult>()));
            state.RoundResults.Add(new RoundResult(2, new Dictionary<Guid, PlayerRoundResult>()));
            state.RoundResults.Add(new RoundResult(3, new Dictionary<Guid, PlayerRoundResult>()));

            state.MatchWinner = WinnerId;
            state.SetPhase(GamePhase.MatchOver);
            return state;
        }

        [TestMethod]
        public void Build_ForWinner_EmitsPersonalAndMatchMetadata()
        {
            var state = BuildMatchOverState();

            var metadata = HiddenAgendaPlayLogMetadata.Build(state, WinnerId);

            Assert.AreEqual("25", metadata["My Score"]);
            Assert.AreEqual("1 / 2", metadata["Placement"]);
            Assert.AreEqual("Won", metadata["Result"]);
            Assert.AreEqual("Bob", metadata["Winner"]);
            Assert.AreEqual("3", metadata["Rounds"]);
            Assert.AreEqual("2", metadata["Players"]);
        }

        [TestMethod]
        public void Build_ForLoser_ReportsLostAndSecondPlace()
        {
            var state = BuildMatchOverState();

            var metadata = HiddenAgendaPlayLogMetadata.Build(state, LoserId);

            Assert.AreEqual("20", metadata["My Score"]);
            Assert.AreEqual("2 / 2", metadata["Placement"]);
            Assert.AreEqual("Lost", metadata["Result"]);
            Assert.AreEqual("Bob", metadata["Winner"]);
        }

        [TestMethod]
        public void Build_ForNonPlayer_OmitsPersonalMetadata()
        {
            var state = BuildMatchOverState();

            var metadata = HiddenAgendaPlayLogMetadata.Build(state, Guid.NewGuid());

            Assert.IsFalse(metadata.ContainsKey("My Score"));
            Assert.IsFalse(metadata.ContainsKey("Placement"));
            Assert.IsFalse(metadata.ContainsKey("Result"));
            Assert.AreEqual("Bob", metadata["Winner"]);
            Assert.AreEqual("3", metadata["Rounds"]);
            Assert.AreEqual("2", metadata["Players"]);
        }
    }
}
