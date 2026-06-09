using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Games.Shared.Projection;
using KnockBox.Core.Services.State.Users;
using KnockBox.DiceSimulator.Contracts;
using KnockBox.DiceSimulator.Services.Logic.Games;
using KnockBox.DiceSimulator.Services.Projection;
using KnockBox.DiceSimulator.Services.State.Games;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.DiceSimulator.Tests.Unit.Projection
{
    /// <summary>
    /// Projection-boundary tests. Dice Simulator has no hidden state, so this is the
    /// "leak test" pattern proved trivially here: the projector emits only a Contracts
    /// DTO (never the server state), and that DTO round-trips through the hub's JSON
    /// wire format — including the enum-keyed die-count map.
    /// </summary>
    [TestClass]
    public class DiceSimulatorStateProjectorTests
    {
        // Mirrors GameViewCoordinator's write options (enums as strings).
        private static readonly JsonSerializerOptions WireWriteOptions = new()
        {
            Converters = { new JsonStringEnumConverter() }
        };

        // Mirrors KnockBox.Core.Client ProjectionJson.DefaultOptions (the browser reader).
        private static readonly JsonSerializerOptions WireReadOptions = new()
        {
            Converters = { new JsonStringEnumConverter() },
            PropertyNameCaseInsensitive = true
        };

        private readonly DiceSimulatorStateProjector _projector = new();

        private static async Task<(DiceSimulatorGameEngine engine, DiceSimulatorGameState state, User host)>
            CreateGameAsync(IRandomNumberService random)
        {
            var engine = new DiceSimulatorGameEngine(
                random,
                Mock.Of<ILogger<DiceSimulatorGameEngine>>(),
                Mock.Of<ILogger<DiceSimulatorGameState>>());
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var result = await engine.CreateStateAsync(host);
            return (engine, (DiceSimulatorGameState)result.Value!, host);
        }

        [TestMethod]
        public async Task ProjectFor_ReturnsContractView_WithRosterLeaderboardAndHistory()
        {
            var random = new Mock<IRandomNumberService>();
            random.Setup(r => r.GetRandomInt(1, 21, RandomType.Fast)).Returns(10);
            var (engine, state, host) = await CreateGameAsync(random.Object);

            var player = UserFactory.Create("Player", Guid.NewGuid());
            state.RegisterPlayer(player);
            engine.RollDice(host, state, new DiceRollAction { DiceType = DiceType.D20, DiceCount = 1, Mode = RollMode.Normal });
            engine.RollDice(player, state, new DiceRollAction { DiceType = DiceType.D20, DiceCount = 1, Mode = RollMode.Normal });

            var view = _projector.ProjectFor(state, host.Id);

            Assert.AreEqual(host.Id, view.HostId);
            Assert.AreEqual(host.Id, view.RecipientId);
            Assert.IsTrue(view.IsJoinable);
            Assert.IsTrue(view.Roster.Any(r => r.PlayerId == host.Id && r.IsHost));
            Assert.IsTrue(view.Roster.Any(r => r.PlayerId == player.Id && !r.IsHost));
            Assert.AreEqual(2, view.Leaderboard.Count);
            Assert.AreEqual(2, view.RollHistory.Count);
        }

        [TestMethod]
        public async Task ProjectFor_RecipientId_ReflectsCaller()
        {
            var (engine, state, host) = await CreateGameAsync(Mock.Of<IRandomNumberService>());
            var player = UserFactory.Create("Player", Guid.NewGuid());
            state.RegisterPlayer(player);

            var view = _projector.ProjectFor(state, player.Id);

            Assert.AreEqual(player.Id, view.RecipientId);
            Assert.AreEqual(host.Id, view.HostId);
            Assert.AreNotEqual(view.RecipientId, view.HostId, "Player should be able to tell it is not the host.");
        }

        [TestMethod]
        public async Task ProjectFor_RoundTripsThroughHubWireFormat_IncludingEnumKeyedDieCounts()
        {
            var random = new Mock<IRandomNumberService>();
            random.Setup(r => r.GetRandomInt(1, 21, RandomType.Fast)).Returns(15);
            var (engine, state, host) = await CreateGameAsync(random.Object);

            engine.RollDice(host, state, new DiceRollAction { DiceType = DiceType.D20, DiceCount = 2, Mode = RollMode.Normal });

            var view = _projector.ProjectFor(state, host.Id);

            // Serialize as the coordinator does, deserialize as the browser does.
            var json = JsonSerializer.Serialize(view, WireWriteOptions);
            var roundTripped = JsonSerializer.Deserialize<DiceSimulatorView>(json, WireReadOptions);

            Assert.IsNotNull(roundTripped);
            Assert.AreEqual(host.Id, roundTripped!.HostId);
            Assert.AreEqual(1, roundTripped.RollHistory.Count);
            Assert.AreEqual(DiceType.D20, roundTripped.RollHistory[0].DiceType);
            Assert.AreEqual(RollMode.Normal, roundTripped.RollHistory[0].Mode);

            // The enum-keyed die-count map must survive as a string-keyed dict ("D20").
            var stats = roundTripped.Leaderboard.Single(s => s.PlayerId == host.Id);
            Assert.AreEqual(2, stats.RollCountByDie["D20"]);
        }

        [TestMethod]
        public async Task Projector_ImplementsUntypedGameStateProjector()
        {
            var (engine, state, _) = await CreateGameAsync(Mock.Of<IRandomNumberService>());

            object? untyped = ((IGameStateProjector)_projector).ProjectFor(state, Guid.NewGuid());

            Assert.IsInstanceOfType<DiceSimulatorView>(untyped);
        }
    }
}
