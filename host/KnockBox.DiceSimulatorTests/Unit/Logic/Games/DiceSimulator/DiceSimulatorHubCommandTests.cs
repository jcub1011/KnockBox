using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Games.Shared.Projection;
using KnockBox.Core.Services.State.Users;
using KnockBox.DiceSimulator.Contracts;
using KnockBox.DiceSimulator.Services.Logic.Games;
using KnockBox.DiceSimulator.Services.State.Games;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.DiceSimulator.Tests.Unit.Logic
{
    /// <summary>
    /// Exercises the hub command boundary: the engine's <see cref="IGameCommandHandler"/>
    /// maps a (command, payloadJson) pair — the shape the WASM client sends over the hub —
    /// to the same engine methods a Razor page used to call, and the resulting state
    /// mutation is reflected by <see cref="IGameStateProjector"/>.
    /// </summary>
    [TestClass]
    public class DiceSimulatorHubCommandTests
    {
        private static readonly JsonSerializerOptions WireWriteOptions = new()
        {
            Converters = { new JsonStringEnumConverter() }
        };

        private Mock<IRandomNumberService> _random = default!;
        private DiceSimulatorGameEngine _engine = default!;
        private DiceSimulatorGameState _state = default!;
        private User _host = default!;

        [TestInitialize]
        public async Task Setup()
        {
            _random = new Mock<IRandomNumberService>();
            _random.Setup(r => r.GetRandomInt(1, 21, RandomType.Fast)).Returns(12);
            _engine = new DiceSimulatorGameEngine(
                _random.Object,
                Mock.Of<ILogger<DiceSimulatorGameEngine>>(),
                Mock.Of<ILogger<DiceSimulatorGameState>>());
            _host = UserFactory.Create("Host", Guid.NewGuid());
            var result = await _engine.CreateStateAsync(_host);
            _state = (DiceSimulatorGameState)result.Value!;
        }

        private ValueTask<KnockBox.Core.Primitives.Returns.Result> Handle(
            User caller, string command, string? payloadJson = null)
            => ((IGameCommandHandler)_engine).HandleCommandAsync(caller, _state, command, payloadJson);

        [TestMethod]
        public async Task RollDiceCommand_MutatesHistory_AndProjectionReflectsIt()
        {
            var payload = JsonSerializer.Serialize(
                new DiceRollAction { DiceType = DiceType.D20, DiceCount = 1, Mode = RollMode.Normal },
                WireWriteOptions);

            var result = await Handle(_host, DiceSimulatorCommands.RollDice, payload);

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.HasCount(1, _state.RollHistory);

            var view = (DiceSimulatorView)((IGameStateProjector)_engine).ProjectFor(_state, _host.Id)!;
            Assert.AreEqual(1, view.RollHistory.Count);
            Assert.AreEqual(12, view.RollHistory[0].Result);
        }

        [TestMethod]
        public async Task RollDiceCommand_MissingPayload_Fails()
        {
            var result = await Handle(_host, DiceSimulatorCommands.RollDice, payloadJson: null);
            Assert.IsTrue((bool)result.IsFailure);
        }

        [TestMethod]
        public async Task RollDiceCommand_MalformedPayload_Fails()
        {
            var result = await Handle(_host, DiceSimulatorCommands.RollDice, "{ not valid json");
            Assert.IsTrue((bool)result.IsFailure);
        }

        [TestMethod]
        public async Task StartCommand_Host_ClosesLobby()
        {
            var result = await Handle(_host, DiceSimulatorCommands.Start);

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.IsFalse(_state.IsJoinable);
        }

        [TestMethod]
        public async Task StartCommand_NonHost_Fails()
        {
            var stranger = UserFactory.Create("Stranger", Guid.NewGuid());
            var result = await Handle(stranger, DiceSimulatorCommands.Start);
            Assert.IsTrue((bool)result.IsFailure);
        }

        [TestMethod]
        public async Task ClearHistoryCommand_NonHost_Fails()
        {
            await Handle(_host, DiceSimulatorCommands.RollDice,
                JsonSerializer.Serialize(new DiceRollAction(), WireWriteOptions));

            var stranger = UserFactory.Create("Stranger", Guid.NewGuid());
            var result = await Handle(stranger, DiceSimulatorCommands.ClearHistory);

            Assert.IsTrue((bool)result.IsFailure);
            Assert.HasCount(1, _state.RollHistory);
        }

        [TestMethod]
        public async Task ClearHistoryCommand_HostFromFreshUserInstance_Clears()
        {
            // The hub resolves a NEW User instance per command (same Id from the token,
            // different object reference). Host authorization must compare by Id, not
            // reference — this reproduces the real over-the-hub path the engine tests miss.
            await Handle(_host, DiceSimulatorCommands.RollDice,
                JsonSerializer.Serialize(new DiceRollAction(), WireWriteOptions));
            Assert.HasCount(1, _state.RollHistory);

            var hostFreshInstance = UserFactory.Create(_host.Name, _host.Id);
            var result = await Handle(hostFreshInstance, DiceSimulatorCommands.ClearHistory);

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.IsEmpty(_state.RollHistory);
        }

        [TestMethod]
        public async Task KickPlayerCommand_Host_RemovesPlayer()
        {
            var player = UserFactory.Create("Player", Guid.NewGuid());
            _state.RegisterPlayer(player);
            Assert.HasCount(1, _state.Players);

            var result = await Handle(_host, DiceSimulatorCommands.KickPlayer, $"\"{player.Id}\"");

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.IsEmpty(_state.Players);
        }

        [TestMethod]
        public async Task UnknownCommand_Fails()
        {
            var result = await Handle(_host, "does-not-exist");
            Assert.IsTrue((bool)result.IsFailure);
        }
    }
}
