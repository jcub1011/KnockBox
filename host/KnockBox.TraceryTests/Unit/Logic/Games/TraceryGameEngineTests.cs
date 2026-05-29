using KnockBox.Tracery.Models;
using KnockBox.Tracery.Services.Logic.Games;
using KnockBox.Tracery.Services.State.Games;
using KnockBox.Core.Services.State.Users;
using KnockBox.WordService.Contracts;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.Tracery.Tests.Unit.Logic.Games
{
    [TestClass]
    public class TraceryGameEngineTests
    {
        private Mock<ILogger<TraceryGameEngine>> _engineLoggerMock = default!;
        private Mock<ILogger<TraceryGameState>> _stateLoggerMock = default!;
        private Mock<IWordListService> _wordListServiceMock = default!;
        private User _host = default!;
        private TraceryGameEngine _engine = default!;

        [TestInitialize]
        public void Setup()
        {
            _engineLoggerMock = new Mock<ILogger<TraceryGameEngine>>();
            _stateLoggerMock = new Mock<ILogger<TraceryGameState>>();
            // These lifecycle tests don't build the dictionary trie, so a bare mock
            // word service is enough — GetSolver is exercised in TracerySolverTests.
            _wordListServiceMock = new Mock<IWordListService>();
            _host = UserFactory.Create("Host", "host1");
            _engine = new TraceryGameEngine(_wordListServiceMock.Object, _engineLoggerMock.Object, _stateLoggerMock.Object);
        }

        // ── Construction / lifecycle ────────────────────────────────────────

        [TestMethod]
        public void PlayerCountRange_IsTwoToEight()
        {
            Assert.AreEqual(2, _engine.MinPlayerCount);
            Assert.AreEqual(8, _engine.MaxPlayerCount);
        }

        [TestMethod]
        public async Task CreateStateAsync_WithHost_ReturnsJoinableState()
        {
            var result = await _engine.CreateStateAsync(_host);

            Assert.IsTrue((bool)result.IsSuccess);
            var state = (TraceryGameState)result.Value!;
            Assert.IsNotNull(state);
            Assert.AreSame(_host, state.Host);
            Assert.IsTrue(state.IsJoinable);
            Assert.AreEqual(GamePhase.Lobby, state.Phase);
        }

        [TestMethod]
        public async Task CreateStateAsync_NullHost_ReturnsError()
        {
            var result = await _engine.CreateStateAsync(null!);

            Assert.IsTrue((bool)result.IsFailure);
        }

        [TestMethod]
        public async Task StartAsync_AsHost_FlipsJoinableOff()
        {
            var state = await CreateStateAsync();

            var result = await _engine.StartAsync(_host, state);

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.IsFalse(state.IsJoinable);
        }

        [TestMethod]
        public async Task StartAsync_NonHost_ReturnsError()
        {
            var state = await CreateStateAsync();
            var stranger = UserFactory.Create("Stranger", "stranger1");

            var result = await _engine.StartAsync(stranger, state);

            Assert.IsTrue((bool)result.IsFailure);
        }

        // ── Start: phase + roster freeze ────────────────────────────────────

        [TestMethod]
        public async Task StartAsync_AsHost_AdvancesPastLobbyAndFreezesParticipants()
        {
            var state = await CreateStateAsync();
            // Long timers so no scheduled callback fires before the synchronous assertions.
            state.UpdateSettings(s => s with { TransitionDuration = TimeSpan.FromMinutes(5) });

            var start = DateTimeOffset.UtcNow;
            var result = await _engine.StartAsync(_host, state);

            Assert.IsTrue((bool)result.IsSuccess);
            Assert.AreEqual(GamePhase.RoundIntro, state.Phase);
            Assert.AreEqual(0, state.CurrentRound);
            Assert.IsNotNull(state.PhaseExpiresAtUtc);
            Assert.IsTrue(state.PhaseExpiresAtUtc!.Value >= start);
            // Host alone → participates, and the frozen roster captures the host.
            Assert.HasCount(1, state.Participants);
            Assert.AreEqual(_host.Id, state.Participants[0].User.Id);
        }

        [TestMethod]
        public async Task StartAsync_HostSolo_HostParticipates()
        {
            var state = await CreateStateAsync();

            await _engine.StartAsync(_host, state);

            Assert.IsTrue(state.HostIsParticipant);
            Assert.IsTrue(state.PlayerStates.ContainsKey(_host.Id));
        }

        [TestMethod]
        public async Task StartAsync_WithOtherPlayers_HostBecomesObserver()
        {
            var state = await CreateStateAsync();
            var players = RegisterPlayers(state, 2);

            await _engine.StartAsync(_host, state);

            Assert.IsFalse(state.HostIsParticipant);
            Assert.IsFalse(state.PlayerStates.ContainsKey(_host.Id));
            Assert.IsTrue(state.PlayerStates.ContainsKey(players[0].Id));
            Assert.IsTrue(state.PlayerStates.ContainsKey(players[1].Id));
        }

        [TestMethod]
        public async Task StartAsync_WithOtherPlayersAndHostPlaysAlong_HostParticipates()
        {
            var state = await CreateStateAsync();
            RegisterPlayers(state, 2);
            state.UpdateSettings(s => s with { HostPlaysAlong = true });

            await _engine.StartAsync(_host, state);

            Assert.IsTrue(state.HostIsParticipant);
            Assert.IsTrue(state.PlayerStates.ContainsKey(_host.Id));
        }

        // ── Placeholder phase progression ───────────────────────────────────

        [TestMethod]
        public async Task DrivingPastLastRound_LandsOnFinalStandings()
        {
            var state = await CreateStateAsync();
            state.UpdateSettings(s => s with
            {
                TotalRounds = 2,
                TransitionDuration = TimeSpan.FromMinutes(5),
                RoundTimer = TimeSpan.FromMinutes(5)
            });

            await _engine.StartAsync(_host, state);
            Assert.AreEqual(GamePhase.RoundIntro, state.Phase);

            // Drive the placeholder flow directly (no wall-clock waits) through both rounds.
            for (int round = 1; round <= 2; round++)
            {
                state.Execute(() => _engine.EnterPlaying(state));
                Assert.AreEqual(GamePhase.Playing, state.Phase);
                Assert.AreEqual(round, state.CurrentRound);

                state.Execute(() => _engine.CompleteRound(state));
                Assert.AreEqual(GamePhase.Reveal, state.Phase);

                state.Execute(() => _engine.EnterRoundOver(state));
                Assert.AreEqual(GamePhase.RoundOver, state.Phase);

                state.Execute(() => _engine.AdvanceAfterResults(state));
            }

            Assert.AreEqual(GamePhase.FinalStandings, state.Phase);
            Assert.IsNull(state.PhaseExpiresAtUtc);
            Assert.HasCount(2, state.RoundResults);
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private async Task<TraceryGameState> CreateStateAsync()
        {
            var result = await _engine.CreateStateAsync(_host);
            Assert.IsTrue(result.TryGetSuccess(out var state));
            return (TraceryGameState)state!;
        }

        private static List<User> RegisterPlayers(TraceryGameState state, int count)
        {
            var players = new List<User>();
            for (int i = 0; i < count; i++)
            {
                var player = UserFactory.Create($"P{i + 1}", Guid.NewGuid().ToString());
                Assert.IsTrue(state.RegisterPlayer(player).IsSuccess);
                players.Add(player);
            }
            return players;
        }
    }
}
