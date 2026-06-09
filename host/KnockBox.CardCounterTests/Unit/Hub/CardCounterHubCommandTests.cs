using System.Text.Json;
using KnockBox.CardCounter.Contracts;
using KnockBox.CardCounter.Services.Logic.Games;
using KnockBox.CardCounter.Services.Projection;
using KnockBox.CardCounter.Services.State.Games;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.CardCounter.Tests.Unit.Hub
{
    /// <summary>
    /// Hub command-handler tests: a command routes to the engine, mutates state, and is
    /// reflected in the projection. Critically includes a host-only command invoked with a
    /// <i>fresh</i> <see cref="User"/> instance carrying the host's id — the hub resolves a
    /// new User per command, so a reference-equality host check would wrongly reject it.
    /// </summary>
    [TestClass]
    public class CardCounterHubCommandTests
    {
        private Mock<IRandomNumberService> _random = default!;
        private CardCounterGameEngine _engine = default!;
        private CardCounterStateProjector _projector = default!;
        private User _host = default!;

        [TestInitialize]
        public void Setup()
        {
            _random = new Mock<IRandomNumberService>();
            _random.Setup(r => r.GetRandomInt(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<RandomType>())).Returns(1);
            _engine = new CardCounterGameEngine(
                _random.Object,
                Mock.Of<ILogger<CardCounterGameEngine>>(),
                Mock.Of<ILogger<CardCounterGameState>>());
            _projector = new CardCounterStateProjector();
            _host = UserFactory.Create("Host", Guid.NewGuid());
        }

        private IGameCommandHandler Handler => _engine;

        private async Task<CardCounterGameState> CreateStateAsync()
            => (CardCounterGameState)(await _engine.CreateStateAsync(_host)).Value!;

        [TestMethod]
        public async Task UpdateSettings_FromFreshHostUserInstance_Succeeds_AndProjectionReflectsIt()
        {
            var state = await CreateStateAsync();

            // A DIFFERENT User object with the host's id — exactly what the hub builds per command.
            var freshHost = UserFactory.Create("Host", _host.Id);
            var payload = JsonSerializer.Serialize(
                new CardCounterSettings { DeckSize = 80 },
                CardCounterContractsJsonContext.Default.CardCounterSettings);

            var result = await Handler.HandleCommandAsync(freshHost, state, CardCounterCommands.UpdateSettings, payload);

            Assert.IsTrue(result.IsSuccess, "Host check must compare by id, not reference.");
            Assert.AreEqual(80, state.Settings.DeckSize);
            Assert.AreEqual(80, _projector.ProjectFor(state, freshHost.Id).Settings.DeckSize);
        }

        [TestMethod]
        public async Task UpdateSettings_FromNonHost_IsRejected()
        {
            var state = await CreateStateAsync();
            var stranger = UserFactory.Create("Stranger", Guid.NewGuid());
            var payload = JsonSerializer.Serialize(
                new CardCounterSettings { DeckSize = 80 },
                CardCounterContractsJsonContext.Default.CardCounterSettings);

            var result = await Handler.HandleCommandAsync(stranger, state, CardCounterCommands.UpdateSettings, payload);

            Assert.IsTrue(result.IsFailure);
            Assert.AreNotEqual(80, state.Settings.DeckSize);
        }

        [TestMethod]
        public async Task SetBuyIn_MutatesPlayerState_AndProjectionReflectsIt()
        {
            var player = UserFactory.Create("Player", Guid.NewGuid());
            var state = await CreateStateAsync();
            state.RegisterPlayer(player);
            await _engine.StartAsync(_host, state);   // → BuyIn phase

            Assert.AreEqual(GamePhase.BuyIn, state.Phase);

            var freshPlayer = UserFactory.Create("Player", player.Id);
            var payload = JsonSerializer.Serialize(
                new SetBuyInPayload(false), CardCounterContractsJsonContext.Default.SetBuyInPayload);

            var result = await Handler.HandleCommandAsync(freshPlayer, state, CardCounterCommands.SetBuyIn, payload);

            Assert.IsTrue(result.IsSuccess);
            Assert.IsTrue(state.GamePlayers[player.Id].HasSetBuyIn);

            var projected = _projector.ProjectFor(state, player.Id).Players.Single(p => p.PlayerId == player.Id);
            Assert.IsTrue(projected.HasSetBuyIn, "Projection must reflect the buy-in mutation.");
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
