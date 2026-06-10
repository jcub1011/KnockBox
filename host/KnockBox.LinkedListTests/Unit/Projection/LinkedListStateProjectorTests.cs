using System.Text.Json;
using System.Text.Json.Serialization;
using KnockBox.Core.Services.State.Games.Shared.Projection;
using KnockBox.Core.Services.State.Users;
using KnockBox.LinkedList.Contracts;
using KnockBox.LinkedList.Services.Logic;
using KnockBox.LinkedList.Services.Logic.Games;
using KnockBox.LinkedList.Services.State.Games;
using KnockBox.LinkedList.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.LinkedList.Tests.Unit.Projection
{
    /// <summary>
    /// Projection security + serialization. The competitive secret is a rival group's chain contents:
    /// a competing player must receive only their own group's chain (MyGroup), never a rival's links —
    /// rivals come through as count-only <see cref="RivalChip"/>s. The Auditor sees only the front
    /// audit-queue group; the host-observer sees every group. The view must also round-trip through
    /// both the hub's reflection serializer and the client's source-gen context (the real WASM path).
    /// </summary>
    [TestClass]
    public class LinkedListStateProjectorTests
    {
        private LinkedListGameEngine _engine = default!;

        // Hub wire format (matches GameViewCoordinator) — string enums, no case-insensitivity needed
        // on the write side; the client reads with the source-gen context.
        private static readonly JsonSerializerOptions WireOptions = new()
        {
            Converters = { new JsonStringEnumConverter() }
        };

        [TestInitialize]
        public void Setup()
        {
            _engine = new LinkedListGameEngine(
                new WordSource(new FakeWordListService()),
                new SequentialRng(),
                NullLogger<LinkedListGameEngine>.Instance,
                NullLogger<LinkedListGameState>.Instance);
        }

        // ── Groups: the rival-chain leak boundary ───────────────────────────────

        [TestMethod]
        public async Task ProjectFor_Groups_CompetingPlayer_SeesOnlyOwnChain_RivalsAreCountsOnly()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var (state, players) = await StartGroupsAsync(host, perGroup: 3, groups: 2, auditorIndex: 5);

            // Finish the rival group so we can assert a finished rival reveals no carried word.
            state.Execute(() => state.GroupById("g1")!.DestinationReached = true);

            var p0 = players[0]; // a competing player in g0
            var view = Project(state, p0.Id);

            Assert.IsNotNull(view.MyGroup, "A competing player must see their own group's chain.");
            Assert.AreEqual("g0", view.MyGroup!.GroupId);
            Assert.AreEqual(0, view.AllGroups.Count, "A competing player must not receive every group's chain.");
            Assert.IsNull(view.AuditingGroup, "A competing player is not the Auditor.");
            Assert.IsFalse(view.RecipientIsAuditor);

            // The only rival channel is a chip — counts only. RivalChip has no Chain/Pending field
            // (compile-time guarantee), and a finished rival's carried word is withheld.
            var rival = view.Rivals.SingleOrDefault(r => r.GroupId == "g1");
            Assert.IsNotNull(rival, "Rivals should surface as chips.");
            Assert.IsTrue(rival!.Finished);
            Assert.IsNull(rival.CarriedWord, "A finished rival's carried word must not be projected.");
        }

        [TestMethod]
        public async Task ProjectFor_Groups_LiveRivalChip_CarriesCarriedWordButNoChain()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var (state, players) = await StartGroupsAsync(host, perGroup: 3, groups: 2, auditorIndex: 5);

            var view = Project(state, players[0].Id);
            var rival = view.Rivals.Single(r => r.GroupId == "g1");

            // A live rival's carried word is shown (existing game reveals it for tension), but the
            // rival's links never travel — the chip type carries no chain.
            Assert.IsFalse(rival.Finished);
            Assert.AreEqual(state.GroupById("g1")!.CarriedWord, rival.CarriedWord);
            Assert.AreEqual(state.GroupById("g1")!.GuessCount, rival.GuessCount);
        }

        [TestMethod]
        public async Task ProjectFor_Groups_HostObserver_SeesEveryChain()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var (state, _) = await StartGroupsAsync(host, perGroup: 3, groups: 2, auditorIndex: 5);

            var view = Project(state, host.Id);

            Assert.IsTrue(view.IsHostObserver);
            Assert.IsNull(view.MyGroup);
            Assert.AreEqual(2, view.AllGroups.Count, "The host-observer sees every group's chain.");
            CollectionAssert.AreEquivalent(new[] { "g0", "g1" }, view.AllGroups.Select(g => g.GroupId).ToList());
        }

        [TestMethod]
        public async Task ProjectFor_Groups_Auditor_SeesOnlyFrontOfQueueGroup()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var (state, players) = await StartGroupsAsync(host, perGroup: 3, groups: 2, auditorIndex: 5);
            var auditor = players[5];

            // The opening submitter of g0 sends a pair, enqueuing g0 for the Auditor.
            var submitter = SubmitterOf(state, "g0", players);
            Assert.IsTrue(_engine.SubmitPair(submitter, state, "BRIDGE").IsSuccess);

            var view = Project(state, auditor.Id);

            Assert.IsTrue(view.RecipientIsAuditor);
            Assert.IsNull(view.MyGroup, "The Auditor isn't projected a playing group.");
            Assert.AreEqual(0, view.AllGroups.Count, "The Auditor must not see every group's chain.");
            Assert.IsNotNull(view.AuditingGroup);
            Assert.AreEqual("g0", view.AuditingGroup!.GroupId);
            Assert.IsNotNull(view.AuditingGroup.Pending);
            Assert.AreEqual(1, view.AuditQueueLength);
        }

        // ── Collective: one shared chain, public to all ─────────────────────────

        [TestMethod]
        public async Task ProjectFor_Collective_SharedChain_VisibleToEveryone()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var (state, players) = await StartCollectiveAsync(host, count: 3);

            // Every participant, the Auditor, and the host-observer all read the single shared chain.
            foreach (var recipient in players.Append(host))
            {
                var view = Project(state, recipient.Id);
                Assert.IsNotNull(view.MyGroup, $"Collective chain must be visible to {recipient.Id}.");
                Assert.AreEqual("all", view.MyGroup!.GroupId);
                Assert.AreEqual(0, view.AllGroups.Count);
                Assert.AreEqual(0, view.Rivals.Count);
            }
        }

        // ── Serialization: hub reflection write → client source-gen read ─────────

        [TestMethod]
        public async Task ProjectFor_RoundTripsThroughHubAndSourceGen()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var (state, players) = await StartGroupsAsync(host, perGroup: 3, groups: 2, auditorIndex: 5);
            var submitter = SubmitterOf(state, "g0", players);
            _engine.SubmitPair(submitter, state, "BRIDGE");

            var view = Project(state, submitter.Id);

            var json = JsonSerializer.Serialize(view, view.GetType(), WireOptions);
            var roundTripped = JsonSerializer.Deserialize(json, LinkedListContractsJsonContext.Default.LinkedListView);

            Assert.IsNotNull(roundTripped);
            Assert.AreEqual(LinkedListGamePhase.Playing, roundTripped!.Phase);
            Assert.AreEqual(PlayerStructure.Groups, roundTripped.Settings.PlayerStructure);
            Assert.IsNotNull(roundTripped.MyGroup);
            Assert.AreEqual("g0", roundTripped.MyGroup!.GroupId);
            Assert.IsNotNull(roundTripped.MyGroup.Pending);
            Assert.AreEqual("BRIDGE", roundTripped.MyGroup.Pending!.ProposedWord);
            Assert.AreEqual(view.StartWord, roundTripped.StartWord);
        }

        [TestMethod]
        public async Task ProjectFor_GameOver_CarriesScoresAndSuperlatives()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var (state, players) = await StartCollectiveAsync(host, count: 3);

            // One accepted pair so an end-of-match superlative is earned.
            var submitter = SubmitterOf(state, "all", players);
            _engine.SubmitPair(submitter, state, "BRIDGE");
            _engine.Approve(AuditorUser(state, players), state);
            _engine.EndMatch(state);

            var view = Project(state, host.Id);

            Assert.AreEqual(LinkedListGamePhase.GameOver, view.Phase);
            Assert.AreEqual(3, view.Scores.Count, "Every participant should appear on the scoreboard.");
            Assert.IsTrue(view.Superlatives.Count > 0, "A played round should award at least one superlative.");
            Assert.IsTrue(view.Scores.Any(s => s.AcceptedPairs > 0));
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private LinkedListView Project(LinkedListGameState state, Guid recipientId)
            => (LinkedListView)((IGameStateProjector)_engine).ProjectFor(state, recipientId)!;

        private async Task<(LinkedListGameState state, List<User> players)> StartGroupsAsync(
            User host, int perGroup, int groups, int auditorIndex)
        {
            var created = await _engine.CreateStateAsync(host);
            var state = (LinkedListGameState)created.Value!;

            var players = new List<User>();
            for (int i = 0; i < perGroup * groups; i++)
            {
                var u = UserFactory.Create($"P{i}", Guid.NewGuid());
                players.Add(u);
                Assert.IsTrue(state.RegisterPlayer(u).IsSuccess);
            }

            var teams = new List<List<Guid>>();
            for (int g = 0; g < groups; g++)
                teams.Add(players.Skip(g * perGroup).Take(perGroup).Select(p => p.Id).ToList());

            var auditorId = players[auditorIndex].Id;
            state.Execute(() =>
            {
                state.GroupAssignments.Clear();
                state.GroupAssignments.AddRange(teams);
                state.AuditorPlayerId = auditorId;
            });
            state.UpdateSettings(s => s with { PlayerStructure = PlayerStructure.Groups });

            Assert.IsTrue((await _engine.StartAsync(host, state)).IsSuccess);
            Assert.AreEqual(LinkedListGamePhase.Playing, state.Phase);
            return (state, players);
        }

        private async Task<(LinkedListGameState state, List<User> players)> StartCollectiveAsync(User host, int count)
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

        private static User SubmitterOf(LinkedListGameState state, string groupId, List<User> players)
        {
            var id = state.GroupById(groupId)!.TurnManager.CurrentPlayer;
            return players.Single(p => p.Id == id);
        }

        private static User AuditorUser(LinkedListGameState state, List<User> players)
            => players.Single(p => p.Id == state.AuditorPlayerId);
    }
}
