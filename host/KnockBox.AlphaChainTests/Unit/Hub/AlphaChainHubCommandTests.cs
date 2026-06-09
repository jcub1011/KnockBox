using System.Text.Json;
using System.Text.Json.Serialization;
using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;
using KnockBox.AlphaChain.Services.Logic.Games.Evaluation;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Tests.Unit.Support;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.State.Games.Shared.Projection;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.AlphaChain.Tests.Unit.Hub
{
    /// <summary>
    /// Exercises the hub command surface (<see cref="IGameCommandHandler"/>) the WASM client drives:
    /// command-name routing, payload deserialization, the <c>User.Id</c> (never reference) caller
    /// comparison, the rejected-word → <c>Result</c> error mapping, and the server tick. Each command
    /// is followed by a <c>ProjectFor</c> to confirm the mutation reaches the projected view.
    /// </summary>
    [TestClass]
    public class AlphaChainHubCommandTests
    {
        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            Converters = { new JsonStringEnumConverter() }
        };

        private User _host = default!;
        private AlphaChainGameEngine _engine = default!;

        [TestInitialize]
        public void Setup()
        {
            _host = UserFactory.Create("Host", Guid.NewGuid());
            _engine = new AlphaChainGameEngine(
                new StubWordListService("cat", "tiger", "dog"),
                new FixedRandomNumberService(),
                new EngineEvaluator(),
                new ModifierCardFactory(),
                Mock.Of<ILogger<AlphaChainGameEngine>>(),
                Mock.Of<ILogger<AlphaChainGameState>>());
        }

        private IGameCommandHandler Hub => _engine;
        private IGameStateProjector_Bridge Projector => new(_engine);

        private async Task<AlphaChainGameState> UnstartedAsync(int players = 2)
        {
            var state = (AlphaChainGameState)(await _engine.CreateStateAsync(_host)).Value!;
            for (int i = 0; i < players; i++)
                state.RegisterPlayer(UserFactory.Create($"P{i}", Guid.NewGuid()));
            state.UpdateSettings(s => s with { EnableTutorials = false });
            return state;
        }

        private async Task<AlphaChainGameState> StartedAsync(int players = 2)
        {
            var state = await UnstartedAsync(players);
            await _engine.StartAsync(_host, state);
            if (state.Phase == AlphaChainGamePhase.Countdown)
                _engine.Tick(state.Context!, state.SubPhaseEndTime.AddSeconds(1));
            return state;
        }

        /// <summary>A different <see cref="User"/> instance carrying the host's id — what the hub
        /// resolves per command. A reference comparison would wrongly reject it.</summary>
        private User FreshHost() => UserFactory.Create("Host-reconnected", _host.Id);

        private static string Json<T>(T payload) => JsonSerializer.Serialize(payload, WriteOptions);

        // ── Host-only commands with a FRESH User (reference-equality footgun guard) ──

        [TestMethod]
        public async Task Start_WithFreshHostUserCarryingHostId_Succeeds()
        {
            using var state = await UnstartedAsync(2);

            var result = await Hub.HandleCommandAsync(
                FreshHost(), state, AlphaChainCommands.Start, Json(new StartPayload(false)));

            Assert.IsTrue(result.IsSuccess, "A fresh host User with the host's id must start the game.");
            Assert.IsFalse(state.IsJoinable);
        }

        [TestMethod]
        public async Task UpdateSettings_NonHost_Rejected_ButFreshHost_Succeeds()
        {
            using var state = await UnstartedAsync(2);
            var stranger = UserFactory.Create("Stranger", Guid.NewGuid());

            var denied = await Hub.HandleCommandAsync(
                stranger, state, AlphaChainCommands.UpdateSettings, Json(new AlphaChainSettings { ShotClockSeconds = 30 }));
            Assert.IsTrue(denied.IsFailure, "Only the host may change settings.");

            var ok = await Hub.HandleCommandAsync(
                FreshHost(), state, AlphaChainCommands.UpdateSettings, Json(new AlphaChainSettings { ShotClockSeconds = 30 }));
            Assert.IsTrue(ok.IsSuccess);
            Assert.AreEqual(30, Projector.View(state, _host.Id).Settings.ShotClockSeconds);
        }

        // ── In-round commands + projection reflects the mutation ──

        [TestMethod]
        public async Task SubmitWord_ByCurrentPlayer_Succeeds_AndAppearsInProjectedFeed()
        {
            using var state = await StartedAsync(2);
            var current = state.TurnManager.CurrentPlayer!.Value;

            var result = await Hub.HandleCommandAsync(
                UserFactory.Create("player", current), state, AlphaChainCommands.SubmitWord, Json(new SubmitWordPayload("cat")));

            Assert.IsTrue(result.IsSuccess, "A valid word by the current player is accepted.");
            var view = Projector.View(state, current);
            Assert.IsTrue(view.PlayFeed.Any(s => s.Word.Equals("cat", StringComparison.OrdinalIgnoreCase)));
        }

        [TestMethod]
        public async Task SubmitWord_OutOfTurn_ReturnsRejectionMessage()
        {
            using var state = await StartedAsync(2);
            var notCurrent = state.TurnManager.TurnOrder.First(id => id != state.TurnManager.CurrentPlayer);

            var result = await Hub.HandleCommandAsync(
                UserFactory.Create("other", notCurrent), state, AlphaChainCommands.SubmitWord, Json(new SubmitWordPayload("tiger")));

            // The typed rejection is surfaced as a Result error the client renders inline.
            Assert.IsTrue(result.IsFailure);
            Assert.IsTrue(result.TryGetFailure(out var error));
            Assert.AreEqual("It's not your turn.", error.PublicMessage);
        }

        [TestMethod]
        public async Task AdvanceTurn_ByCurrentPlayer_RotatesTurn()
        {
            using var state = await StartedAsync(2);
            var first = state.TurnManager.CurrentPlayer!.Value;

            var result = await Hub.HandleCommandAsync(
                UserFactory.Create("player", first), state, AlphaChainCommands.AdvanceTurn, payloadJson: null);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreNotEqual(first, Projector.View(state, first).CurrentPlayerId);
        }

        [TestMethod]
        public async Task UnknownCommand_ReturnsError()
        {
            using var state = await StartedAsync(2);

            var result = await Hub.HandleCommandAsync(_host, state, "no-such-command", payloadJson: null);

            Assert.IsTrue(result.IsFailure);
        }

        // ── Server tick drives the FSM (replaces the old host-circuit tick) ──

        [TestMethod]
        public async Task ServerTick_DrivesCountdownIntoRound()
        {
            using var state = await UnstartedAsync(2);
            await _engine.StartAsync(_host, state);
            Assert.AreEqual(AlphaChainGamePhase.Countdown, state.Phase);

            ((IServerTickHandler)_engine).Tick(state, state.SubPhaseEndTime.AddSeconds(1));

            Assert.AreEqual(AlphaChainGamePhase.Round, state.Phase);
        }

        /// <summary>Tiny bridge so a test can call the engine's untyped projector entry point
        /// and get a strongly-typed <see cref="AlphaChainView"/> back.</summary>
        private readonly struct IGameStateProjector_Bridge(AlphaChainGameEngine engine)
        {
            public AlphaChainView View(AlphaChainGameState state, Guid recipientId)
                => (AlphaChainView)((IGameStateProjector)engine).ProjectFor(state, recipientId)!;
        }
    }
}
