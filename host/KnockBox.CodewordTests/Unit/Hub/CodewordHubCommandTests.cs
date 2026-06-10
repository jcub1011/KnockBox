using System.Text.Json;
using KnockBox.Codeword.Contracts;
using KnockBox.Codeword.Services.Logic.Games;
using KnockBox.Codeword.Services.Projection;
using KnockBox.Codeword.Services.State.Games;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.Codeword.Tests.Unit.Hub
{
    /// <summary>
    /// Hub command-handler tests: a command routes to the engine, mutates state, and is
    /// reflected in the projection. Critically includes host-only commands invoked with a
    /// <i>fresh</i> <see cref="User"/> instance carrying the host's id — the hub resolves a
    /// new User per command, so a reference-equality host check would wrongly reject it.
    /// Also exercises the server tick handler that drives the timed FSM.
    /// </summary>
    [TestClass]
    public class CodewordHubCommandTests
    {
        private Mock<IRandomNumberService> _random = default!;
        private CodewordGameEngine _engine = default!;
        private CodewordStateProjector _projector = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            _random = new Mock<IRandomNumberService>();
            _random.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<RandomType>()))
                .Returns((int max, RandomType _) => 0);
            _random.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<RandomType>()))
                .Returns((int min, int max, RandomType _) => min);
            _engine = new CodewordGameEngine(
                _random.Object,
                Mock.Of<ILogger<CodewordGameEngine>>(),
                Mock.Of<ILogger<CodewordGameState>>());
            _projector = new CodewordStateProjector();
            _host = UserFactory.Create("Host", Guid.NewGuid());
        }

        private IGameCommandHandler Handler => _engine;

        private async Task<CodewordGameState> CreateStateAsync()
            => (CodewordGameState)(await _engine.CreateStateAsync(_host)).Value!;

        private static async Task<CodewordGameState> WithPlayersAsync(CodewordGameState state, int count)
        {
            for (int i = 0; i < count; i++)
                state.RegisterPlayer(UserFactory.Create($"Player{i}", Guid.NewGuid()));
            return await Task.FromResult(state);
        }

        [TestMethod]
        public async Task UpdateSettings_FromFreshHostUserInstance_Succeeds_AndProjectionReflectsIt()
        {
            var state = await CreateStateAsync();

            // A DIFFERENT User object with the host's id — exactly what the hub builds per command.
            var freshHost = UserFactory.Create("Host", _host.Id);
            var payload = JsonSerializer.Serialize(
                new CodewordSettings { TotalGames = 9 },
                CodewordContractsJsonContext.Default.CodewordSettings);

            var result = await Handler.HandleCommandAsync(freshHost, state, CodewordCommands.UpdateSettings, payload);

            Assert.IsTrue(result.IsSuccess, "Host check must compare by id, not reference.");
            Assert.AreEqual(9, state.Settings.TotalGames);
            Assert.AreEqual(9, _projector.ProjectFor(state, freshHost.Id).Settings.TotalGames);
        }

        [TestMethod]
        public async Task UpdateSettings_FromNonHost_IsRejected()
        {
            var state = await CreateStateAsync();
            var stranger = UserFactory.Create("Stranger", Guid.NewGuid());
            var payload = JsonSerializer.Serialize(
                new CodewordSettings { TotalGames = 9 },
                CodewordContractsJsonContext.Default.CodewordSettings);

            var result = await Handler.HandleCommandAsync(stranger, state, CodewordCommands.UpdateSettings, payload);

            Assert.IsTrue(result.IsFailure);
            Assert.AreNotEqual(9, state.Settings.TotalGames);
        }

        [TestMethod]
        public async Task Start_FromFreshHostUser_StartsGame_AndProjectionAssignsRecipientRole()
        {
            var state = await CreateStateAsync();
            await WithPlayersAsync(state, 4);   // 4 participants — the minimum to start.

            var freshHost = UserFactory.Create("Host", _host.Id);
            var payload = JsonSerializer.Serialize(
                new StartPayload(false), CodewordContractsJsonContext.Default.StartPayload);

            var result = await Handler.HandleCommandAsync(freshHost, state, CodewordCommands.Start, payload);

            Assert.IsTrue(result.IsSuccess, "Host check on start must compare by id, not reference.");
            Assert.IsFalse(state.IsJoinable, "Starting the game closes the lobby.");

            // The recipient (a player) learns their own assigned role through the projection.
            var aPlayerId = state.GamePlayers.Keys.First();
            Assert.IsNotNull(_projector.ProjectFor(state, aPlayerId).MyRole);
        }

        [TestMethod]
        public async Task ServerTick_AfterSetupTimeout_AdvancesPhaseToCluePhase()
        {
            var state = await CreateStateAsync();
            await WithPlayersAsync(state, 4);
            await _engine.StartAsync(_host, state);   // → Setup phase
            Assert.AreEqual(CodewordGamePhase.Setup, state.Phase);

            // The platform's LobbyTickService calls this ~4 Hz; fast-forward past the setup timeout.
            ((IServerTickHandler)_engine).Tick(state, DateTimeOffset.UtcNow.AddMinutes(5));

            Assert.AreEqual(CodewordGamePhase.CluePhase, state.Phase);
            Assert.AreEqual(CodewordGamePhase.CluePhase, _projector.ProjectFor(state, _host.Id).Phase);
        }

        [TestMethod]
        public async Task MalformedPayload_IsRejected()
        {
            var state = await CreateStateAsync();
            var result = await Handler.HandleCommandAsync(_host, state, CodewordCommands.CastVote, "{not valid json");
            Assert.IsTrue(result.IsFailure);
        }

        [TestMethod]
        public async Task UnknownCommand_IsRejected()
        {
            var state = await CreateStateAsync();
            var result = await Handler.HandleCommandAsync(_host, state, "not-a-command", null);
            Assert.IsTrue(result.IsFailure);
        }
    }
}
