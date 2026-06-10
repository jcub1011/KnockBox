using System.Text.Json;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.LinkedList.Contracts;
using KnockBox.LinkedList.Services.Logic;
using KnockBox.LinkedList.Services.Logic.Games;
using KnockBox.LinkedList.Services.State.Games;
using KnockBox.LinkedList.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.LinkedList.Tests.Unit.Hub
{
    /// <summary>
    /// The hub command surface (<see cref="IGameCommandHandler"/>): each command maps to the engine
    /// method a Razor page used to call directly. The hub resolves a FRESH <see cref="User"/> per
    /// command from the connection token, so host/auditor-gated commands must compare by
    /// <c>User.Id</c>, never by reference — these tests pass a different User instance carrying the
    /// gated player's id to guard that footgun.
    /// </summary>
    [TestClass]
    public class LinkedListHubCommandTests
    {
        private LinkedListGameEngine _engine = default!;
        private IGameCommandHandler Hub => _engine;

        private static User Fresh(Guid id) => UserFactory.Create("reconnected", id);

        [TestInitialize]
        public void Setup()
        {
            _engine = new LinkedListGameEngine(
                new WordSource(new FakeWordListService()),
                new SequentialRng(),
                NullLogger<LinkedListGameEngine>.Instance,
                NullLogger<LinkedListGameState>.Instance);
        }

        [TestMethod]
        public async Task SubmitPair_Command_AddsPendingSubmission()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var (state, players) = await PlayingCollectiveAsync(host, 3);
            var submitter = SubmitterOf(state, players);

            var result = await Hub.HandleCommandAsync(Fresh(submitter.Id), state, LinkedListCommands.SubmitPair,
                JsonSerializer.Serialize(new SubmitPairPayload("BRIDGE"), LinkedListContractsJsonContext.Default.SubmitPairPayload));

            Assert.IsTrue(result.IsSuccess);
            Assert.IsNotNull(state.PrimaryGroup!.PendingSubmission);
            Assert.AreEqual("BRIDGE", state.PrimaryGroup.PendingSubmission!.ProposedWord);
        }

        [TestMethod]
        public async Task Approve_FreshAuditorUser_AdvancesChain()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var (state, players) = await PlayingCollectiveAsync(host, 3);
            var submitter = SubmitterOf(state, players);
            _engine.SubmitPair(submitter, state, "BRIDGE");
            var auditorId = state.AuditorPlayerId;

            var result = await Hub.HandleCommandAsync(Fresh(auditorId), state, LinkedListCommands.Approve, null);

            Assert.IsTrue(result.IsSuccess, "A fresh Auditor User (id match) must be able to approve.");
            Assert.AreEqual(1, state.PrimaryGroup!.Chain.Count);
            Assert.IsNull(state.PrimaryGroup.PendingSubmission);
        }

        [TestMethod]
        public async Task Reject_NonAuditor_IsRejected()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var (state, players) = await PlayingCollectiveAsync(host, 3);
            var submitter = SubmitterOf(state, players);
            _engine.SubmitPair(submitter, state, "BRIDGE");

            // A stranger (not the Auditor) cannot reject.
            var stranger = UserFactory.Create("Stranger", Guid.NewGuid());
            var result = await Hub.HandleCommandAsync(stranger, state, LinkedListCommands.Reject, null);

            Assert.IsTrue(result.IsFailure);
            Assert.IsNotNull(state.PrimaryGroup!.PendingSubmission, "A rejected reject leaves the submission pending.");
        }

        [TestMethod]
        public async Task EndRound_StrangerRejected_FreshHostEndsRound()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var (state, _) = await PlayingCollectiveAsync(host, 3);

            var stranger = UserFactory.Create("Stranger", Guid.NewGuid());
            Assert.IsTrue((await Hub.HandleCommandAsync(stranger, state, LinkedListCommands.EndRound, null)).IsFailure);

            Assert.IsTrue((await Hub.HandleCommandAsync(Fresh(host.Id), state, LinkedListCommands.EndRound, null)).IsSuccess);
            Assert.AreEqual(LinkedListGamePhase.RoundOver, state.Phase);
        }

        [TestMethod]
        public async Task NextRoundAndEndMatch_AreHostGated()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var (state, _) = await PlayingCollectiveAsync(host, 3);
            var stranger = UserFactory.Create("Stranger", Guid.NewGuid());

            Assert.IsTrue((await Hub.HandleCommandAsync(stranger, state, LinkedListCommands.NextRound, null)).IsFailure);
            Assert.IsTrue((await Hub.HandleCommandAsync(stranger, state, LinkedListCommands.EndMatch, null)).IsFailure);
            Assert.AreEqual(LinkedListGamePhase.Playing, state.Phase, "A rejected host command must not advance the match.");
        }

        [TestMethod]
        public async Task EndMatchThenReturnToLobby_HostFlow_ReopensLobby()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var (state, _) = await PlayingCollectiveAsync(host, 3);

            Assert.IsTrue((await Hub.HandleCommandAsync(Fresh(host.Id), state, LinkedListCommands.EndMatch, null)).IsSuccess);
            Assert.AreEqual(LinkedListGamePhase.GameOver, state.Phase);

            // Non-host can't return to the lobby; the fresh host can.
            var stranger = UserFactory.Create("Stranger", Guid.NewGuid());
            Assert.IsTrue((await Hub.HandleCommandAsync(stranger, state, LinkedListCommands.ReturnToLobby, null)).IsFailure);

            Assert.IsTrue((await Hub.HandleCommandAsync(Fresh(host.Id), state, LinkedListCommands.ReturnToLobby, null)).IsSuccess);
            Assert.IsTrue(state.IsJoinable);
            Assert.AreEqual(LinkedListGamePhase.Setup, state.Phase);
        }

        [TestMethod]
        public async Task KickPlayer_Command_RemovesPlayer()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var state = await LobbyAsync(host, 3);
            var victim = state.Players[0].User;

            var result = await Hub.HandleCommandAsync(Fresh(host.Id), state, LinkedListCommands.KickPlayer,
                JsonSerializer.Serialize(new KickPlayerPayload(victim.Id), LinkedListContractsJsonContext.Default.KickPlayerPayload));

            Assert.IsTrue(result.IsSuccess);
            Assert.IsFalse(state.Players.Any(p => p.User.Id == victim.Id));
        }

        [TestMethod]
        public async Task Start_Command_AppliesPayloadTeamsAuditorAndWords()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var state = await LobbyAsync(host, 6);
            state.UpdateSettings(s => s with { PlayerStructure = PlayerStructure.Groups });

            var ids = state.Players.Select(p => p.User.Id).ToList();
            var teams = new List<List<Guid>> { ids.Take(3).ToList(), ids.Skip(3).Take(3).ToList() };
            var auditorId = ids[5];
            var payload = new StartPayload(false, teams, auditorId, "ALPHA", "OMEGA");

            var result = await Hub.HandleCommandAsync(Fresh(host.Id), state, LinkedListCommands.Start,
                JsonSerializer.Serialize(payload, LinkedListContractsJsonContext.Default.StartPayload));

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(LinkedListGamePhase.Playing, state.Phase);
            Assert.AreEqual(2, state.Groups.Count);
            Assert.AreEqual(auditorId, state.AuditorPlayerId);
            Assert.AreEqual("ALPHA", state.StartWord);
            Assert.AreEqual("OMEGA", state.DestinationWord);
            Assert.IsFalse(state.HostIsParticipant);
        }

        [TestMethod]
        public async Task Start_Command_HostPlaysVariant_SeatsHost()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var state = await LobbyAsync(host, 3);
            var payload = new StartPayload(true, null, null, null, null);

            var result = await Hub.HandleCommandAsync(Fresh(host.Id), state, LinkedListCommands.Start,
                JsonSerializer.Serialize(payload, LinkedListContractsJsonContext.Default.StartPayload));

            Assert.IsTrue(result.IsSuccess);
            Assert.IsTrue(state.HostIsParticipant);
            Assert.IsTrue(state.GamePlayers.ContainsKey(host.Id), "A host who plays must be seated as a participant.");
        }

        [TestMethod]
        public async Task UpdateSettings_PreservesServerOnlyHostPlays_AndIsHostGated()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var state = await LobbyAsync(host, 3);
            state.UpdateSettings(s => s with { HostPlays = true });

            var view = new LinkedListSettingsView { RoundsPerMatch = 7 };
            var payload = JsonSerializer.Serialize(view, LinkedListContractsJsonContext.Default.LinkedListSettingsView);

            var stranger = UserFactory.Create("Stranger", Guid.NewGuid());
            Assert.IsTrue((await Hub.HandleCommandAsync(stranger, state, LinkedListCommands.UpdateSettings, payload)).IsFailure);

            Assert.IsTrue((await Hub.HandleCommandAsync(Fresh(host.Id), state, LinkedListCommands.UpdateSettings, payload)).IsSuccess);
            Assert.AreEqual(7, state.Settings.RoundsPerMatch);
            Assert.IsTrue(state.Settings.HostPlays, "The settings view omits HostPlays, so applying it must preserve the server value.");
        }

        [TestMethod]
        public async Task UnknownCommand_ReturnsError()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var state = await LobbyAsync(host, 3);

            var result = await Hub.HandleCommandAsync(host, state, "no-such-command", null);

            Assert.IsTrue(result.IsFailure);
            Assert.IsTrue(result.TryGetFailure(out var err));
            StringAssert.Contains(err.PublicMessage, "Unknown command");
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private async Task<LinkedListGameState> LobbyAsync(User host, int playerCount)
        {
            var created = await _engine.CreateStateAsync(host);
            var state = (LinkedListGameState)created.Value!;
            for (int i = 0; i < playerCount; i++)
                Assert.IsTrue(state.RegisterPlayer(UserFactory.Create($"P{i}", Guid.NewGuid())).IsSuccess);
            return state;
        }

        private async Task<(LinkedListGameState state, List<User> players)> PlayingCollectiveAsync(User host, int count)
        {
            var created = await _engine.CreateStateAsync(host);
            var state = (LinkedListGameState)created.Value!;
            var players = new List<User>();
            for (int i = 0; i < count; i++)
            {
                var u = UserFactory.Create($"P{i}", Guid.NewGuid());
                players.Add(u);
                Assert.IsTrue(state.RegisterPlayer(u).IsSuccess);
            }
            Assert.IsTrue((await _engine.StartAsync(host, state)).IsSuccess);
            Assert.AreEqual(LinkedListGamePhase.Playing, state.Phase);
            return (state, players);
        }

        private static User SubmitterOf(LinkedListGameState state, List<User> players)
        {
            var id = state.PrimaryGroup!.TurnManager.CurrentPlayer;
            return players.Single(p => p.Id == id);
        }
    }
}
