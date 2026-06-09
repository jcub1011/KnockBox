using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using KnockBox.Tracery.Contracts;
using KnockBox.Tracery.Models;
using KnockBox.Tracery.Services.Logic.Games;
using KnockBox.Tracery.Services.State.Games;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.State.Games.Shared.Projection;
using KnockBox.Core.Services.State.Users;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.WordService.Contracts;
using KnockBox.WordService.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.Tracery.Tests.Unit.Hub
{
    /// <summary>
    /// The hub command surface (<see cref="IGameCommandHandler"/>): each command maps to the engine
    /// method a Razor page used to call directly. Critically, the hub resolves a FRESH
    /// <see cref="User"/> per command from the connection token, so host-gated commands must compare
    /// by <c>User.Id</c>, never by reference — these tests pass a different User instance carrying the
    /// host's id to guard that footgun.
    /// </summary>
    [TestClass]
    public class TraceryHubCommandTests
    {
        private static TraceryGameEngine _engine = default!;

        private static Grid MakeBoard() => new(3, 3, "tabrcepod");
        private static IGameCommandHandler Hub => _engine;

        // A different User instance carrying the host's id — what the hub resolves per command.
        private static User FreshHost(User host) => UserFactory.Create("Host-reconnected", host.Id);

        [ClassInitialize]
        public static void ClassInit(TestContext _)
        {
            var svc = new WordListService(NullLogger<WordListService>.Instance);
            _engine = new TraceryGameEngine(
                svc, new RandomNumberService(),
                NullLogger<TraceryGameEngine>.Instance, NullLogger<TraceryGameState>.Instance);
        }

        [TestMethod]
        public async Task SubmitTrace_Command_BanksWordAndProjectsIt()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var state = await PlayingSoloAsync(host);
            var (word, path) = FirstFindable();

            var error = await Hub.HandleCommandAsync(host, state, TraceryCommands.SubmitTrace,
                Json(new SubmitTracePayload(path), TraceryContractsJsonContext.Default.SubmitTracePayload));

            Assert.IsTrue(error.IsSuccess, "A valid trace command should succeed.");
            Assert.IsTrue(state.TryGetPlayerState(host.Id, out var ps));
            Assert.IsTrue(ps.HasBanked(word));

            // The mutation is reflected in the projection.
            var view = (TraceryView)((IGameStateProjector)_engine).ProjectFor(state, host.Id)!;
            CollectionAssert.Contains(view.MyBankedWords.Select(b => b.Word).ToList(), word);
        }

        [TestMethod]
        public async Task UpdateSettings_FreshHostUser_Succeeds_NonHostRejected()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var state = await LobbyAsync(host);
            var payload = Json(new TracerySettingsView { TotalRounds = 7 },
                TraceryContractsJsonContext.Default.TracerySettingsView);

            var stranger = UserFactory.Create("Stranger", Guid.NewGuid());
            var denied = await Hub.HandleCommandAsync(stranger, state, TraceryCommands.UpdateSettings, payload);
            Assert.IsTrue(denied.IsFailure, "Only the host may change settings.");

            // A fresh host User (host's id, different instance) must be accepted (id comparison, not reference).
            var ok = await Hub.HandleCommandAsync(FreshHost(host), state, TraceryCommands.UpdateSettings, payload);
            Assert.IsTrue(ok.IsSuccess);
            Assert.AreEqual(7, state.Settings.TotalRounds);
        }

        [TestMethod]
        public async Task SkipReveal_Command_FreshHost_AdvancesPhase()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var state = await PlayingSoloAsync(host);
            // Close the round so the game is in the Reveal phase.
            state.Execute(() => _engine.CompleteRound(state));
            Assert.AreEqual(GamePhase.Reveal, state.Phase);

            var result = await Hub.HandleCommandAsync(FreshHost(host), state, TraceryCommands.SkipReveal, null);

            Assert.IsTrue(result.IsSuccess, "A fresh host User must be able to skip the reveal.");
            Assert.AreNotEqual(GamePhase.Reveal, state.Phase, "Skipping the reveal advances the phase.");
        }

        [TestMethod]
        public async Task UnknownCommand_ReturnsError()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var state = await LobbyAsync(host);

            var result = await Hub.HandleCommandAsync(host, state, "no-such-command", null);

            Assert.IsTrue(result.IsFailure);
            Assert.IsTrue(result.TryGetFailure(out var err));
            StringAssert.Contains(err.PublicMessage, "Unknown command");
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static string Json<T>(T payload, JsonTypeInfo<T> info) => JsonSerializer.Serialize(payload, info);

        private static async Task<TraceryGameState> LobbyAsync(User host)
        {
            var created = await _engine.CreateStateAsync(host);
            Assert.IsTrue(created.TryGetSuccess(out var s));
            var state = (TraceryGameState)s!;
            state.UpdateSettings(x => x with
            {
                RoundTimer = TimeSpan.FromMinutes(5),
                TransitionDuration = TimeSpan.FromMinutes(5),
                IntermissionDuration = TimeSpan.FromMinutes(5),
                MinWordLength = 3,
            });
            return state;
        }

        private static async Task<TraceryGameState> PlayingSoloAsync(User host)
        {
            var state = await LobbyAsync(host);
            await _engine.StartAsync(host, state);
            state.Execute(() => _engine.EnterPlaying(state));
            state.Execute(() => state.CurrentGrid = MakeBoard());
            return state;
        }

        private static (string word, int[] path) FirstFindable()
        {
            var found = _engine.GetSolver(WordPoolMode.FullDictionary).Solve(MakeBoard(), minWordLength: 3);
            var entry = found.Values.First();
            return (entry.Word, entry.Path.ToArray());
        }
    }
}
