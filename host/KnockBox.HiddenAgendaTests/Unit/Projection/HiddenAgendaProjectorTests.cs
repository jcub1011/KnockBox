using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using KnockBox.Core.Services.State.Users;
using KnockBox.HiddenAgenda.Services.Logic.Games.Data;
using KnockBox.HiddenAgenda.Services.Projection;
using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.HiddenAgenda.Services.State.Games.Data;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace KnockBox.HiddenAgendaTests.Unit.Projection
{
    /// <summary>
    /// Phase 0 kill-criterion #2: prove the per-player projection is default-deny —
    /// a player's projected view never carries another player's secrets. This is
    /// the template for the per-game leak tests every migrated game will need.
    /// </summary>
    [TestClass]
    public class HiddenAgendaProjectorTests
    {
        private static readonly Guid PlayerA = Guid.Parse("0000000A-0000-0000-0000-000000000000");
        private static readonly Guid PlayerB = Guid.Parse("0000000B-0000-0000-0000-000000000000");
        private static readonly Guid PlayerC = Guid.Parse("0000000C-0000-0000-0000-000000000000");
        private static readonly Guid BRivalryMarker = Guid.Parse("BBBB0001-0000-0000-0000-000000000000");

        private HiddenAgendaGameState _state = default!;

        [TestInitialize]
        public void Setup()
        {
            var host = UserFactory.Create("Host", Guid.Parse("00000000-0000-0000-0000-000000000001"));
            _state = new HiddenAgendaGameState(host, Mock.Of<ILogger<HiddenAgendaGameState>>());

            // Distinct, non-overlapping secret-task slices so each player's secrets
            // are uniquely attributable (no false negatives from shared tasks).
            var pool = TaskPool.AllTasks;
            AddPlayer(PlayerA, "Alice", pool.Skip(0).Take(3));
            AddPlayer(PlayerB, "Bob", pool.Skip(3).Take(3));
            AddPlayer(PlayerC, "Cara", pool.Skip(6).Take(3));

            // Give Bob extra unique secrets so we can assert each kind is redacted.
            _state.GamePlayers[PlayerB].RivalryTargetPlayerId = BRivalryMarker;
            _state.GamePlayers[PlayerB].HeldEventCard = new EventCard(default, "LEAKMARKER_BOB_EVENT_CARD");
            _state.GamePlayers[PlayerB].GuessSubmission = new Dictionary<Guid, List<string>>
            {
                [PlayerA] = ["LEAKMARKER_BOB_GUESS_TASK"]
            };
        }

        private void AddPlayer(Guid id, string name, IEnumerable<SecretTask> tasks)
            => _state.GamePlayers[id] = new HiddenAgendaPlayerState
            {
                PlayerId = id,
                DisplayName = name,
                SecretTasks = tasks.ToList()
            };

        [TestMethod]
        public void ProjectFor_IncludesRecipientOwnSecrets()
        {
            var view = HiddenAgendaProjector.ProjectFor(_state, PlayerA);

            var alice = view.Players.Single(p => p.PlayerId == PlayerA);
            Assert.IsNotNull(alice.SecretTasks);
            CollectionAssert.AreEquivalent(
                _state.GamePlayers[PlayerA].SecretTasks.Select(t => t.Id).ToList(),
                alice.SecretTasks!.Select(t => t.Id).ToList());
        }

        [TestMethod]
        public void ProjectFor_RedactsOtherPlayersSecretFields()
        {
            var view = HiddenAgendaProjector.ProjectFor(_state, PlayerA);

            foreach (var other in view.Players.Where(p => p.PlayerId != PlayerA))
            {
                Assert.IsNull(other.SecretTasks, $"{other.DisplayName} secret tasks leaked.");
                Assert.IsNull(other.RivalryTargetPlayerId, $"{other.DisplayName} rivalry target leaked.");
                Assert.IsNull(other.HeldEventCard, $"{other.DisplayName} held event card leaked.");
                Assert.IsNull(other.GuessSubmission, $"{other.DisplayName} guess submission leaked.");
            }
        }

        [TestMethod]
        public void ProjectFor_SerializedView_ContainsNoOtherPlayerSecrets()
        {
            var view = HiddenAgendaProjector.ProjectFor(_state, PlayerA);

            // Serialize with the SAME reflection-based serializer the hub uses on
            // the wire, then assert at the byte level that no foreign secret value
            // is present in what would reach Alice's browser.
            var json = JsonSerializer.Serialize(view, view.GetType());

            // Bob's and Cara's secret task descriptions (Alice does not share them).
            foreach (var foreign in new[] { PlayerB, PlayerC })
            {
                foreach (var task in _state.GamePlayers[foreign].SecretTasks)
                {
                    AssertAbsent(json, task.Description);
                    AssertAbsent(json, task.ObservablePattern);
                }
            }

            // Bob's other secret kinds.
            AssertAbsent(json, BRivalryMarker.ToString());
            AssertAbsent(json, "LEAKMARKER_BOB_EVENT_CARD");
            AssertAbsent(json, "LEAKMARKER_BOB_GUESS_TASK");

            // Sanity: Alice's own secrets ARE present (the projection isn't empty).
            foreach (var task in _state.GamePlayers[PlayerA].SecretTasks)
                StringAssert.Contains(json, task.Description);
        }

        private static void AssertAbsent(string json, string secret)
            => Assert.IsFalse(
                json.Contains(secret, StringComparison.Ordinal),
                $"Projected view leaked a foreign secret: [{secret}].");
    }
}
