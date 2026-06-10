using System.Text.Json;
using System.Text.Json.Serialization;
using KnockBox.Codeword.Contracts;
using KnockBox.Codeword.Services.Projection;
using KnockBox.Codeword.Services.State.Games;
using KnockBox.Codeword.Services.State.Games.Data;
using KnockBox.Core.Services.State.Games.Shared.Projection;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.Codeword.Tests.Unit.Projection
{
    /// <summary>
    /// Projection-boundary tests — the security headline for Codeword. The hidden word pair
    /// must never cross the wire (it is reconstructable from any two players' secret words),
    /// and a recipient may learn ONLY their own role + secret word. Other players' roles
    /// surface only when publicly revealed. Also verifies the view round-trips through the
    /// hub's JSON format, including the string-keyed <c>GameScores</c> dictionary.
    /// </summary>
    [TestClass]
    public class CodewordStateProjectorTests
    {
        private const string AgentWord = "OCEAN";
        private const string InsiderWord = "DESERT";

        // Mirrors GameViewCoordinator's write options (enums as strings).
        private static readonly JsonSerializerOptions WireWriteOptions = new()
        {
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly CodewordStateProjector _projector = new();

        /// <summary>
        /// 5-player started state (3 Agents / 1 Insider / 1 Informant), roles + secret words
        /// assigned deterministically so the leak tests know exactly which word belongs to whom.
        /// </summary>
        private static CodewordGameState BuildStartedState(
            out Guid agentId, out Guid insiderId, out Guid informantId)
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var state = new CodewordGameState(host, Mock.Of<ILogger<CodewordGameState>>());

            agentId = Guid.NewGuid();
            insiderId = Guid.NewGuid();
            informantId = Guid.NewGuid();
            var agent2 = Guid.NewGuid();
            var agent3 = Guid.NewGuid();

            void Add(Guid id, string name, Role role, string? word)
            {
                state.GamePlayers[id] = new CodewordPlayerState
                {
                    PlayerId = id,
                    DisplayName = name,
                    Role = role,
                    SecretWord = word
                };
                state.TurnManager.TurnOrder.Add(id);
            }

            Add(agentId, "Agent1", Role.Agent, AgentWord);
            Add(agent2, "Agent2", Role.Agent, AgentWord);
            Add(agent3, "Agent3", Role.Agent, AgentWord);
            Add(insiderId, "Insider", Role.Insider, InsiderWord);
            Add(informantId, "Informant", Role.Informant, null);

            state.CurrentWordPair = [AgentWord, InsiderWord];
            state.Execute(() => state.SetPhase(CodewordGamePhase.CluePhase));
            return state;
        }

        [TestMethod]
        public void ProjectFor_GivesRecipientOwnRoleAndWord()
        {
            var state = BuildStartedState(out var agentId, out var insiderId, out var informantId);

            var agentView = _projector.ProjectFor(state, agentId);
            Assert.AreEqual(Role.Agent, agentView.MyRole);
            Assert.AreEqual(AgentWord, agentView.MySecretWord);

            var insiderView = _projector.ProjectFor(state, insiderId);
            Assert.AreEqual(Role.Insider, insiderView.MyRole);
            Assert.AreEqual(InsiderWord, insiderView.MySecretWord);

            var informantView = _projector.ProjectFor(state, informantId);
            Assert.AreEqual(Role.Informant, informantView.MyRole);
            Assert.IsNull(informantView.MySecretWord, "The Informant has no secret word.");
        }

        [TestMethod]
        public void ProjectFor_DoesNotLeakTheOpposingWord_OrTheWordPair()
        {
            var state = BuildStartedState(out var agentId, out var insiderId, out _);

            // An Agent's view must never contain the Insider word...
            var agentJson = JsonSerializer.Serialize(_projector.ProjectFor(state, agentId), WireWriteOptions);
            Assert.IsFalse(agentJson.Contains(InsiderWord, StringComparison.OrdinalIgnoreCase),
                "An Agent's projection must never contain the Insider word (the word pair never crosses).");

            // ...and an Insider's view must never contain the Agent word. Together this proves
            // CurrentWordPair never crosses and the pair can't be reconstructed from one view.
            var insiderJson = JsonSerializer.Serialize(_projector.ProjectFor(state, insiderId), WireWriteOptions);
            Assert.IsFalse(insiderJson.Contains(AgentWord, StringComparison.OrdinalIgnoreCase),
                "An Insider's projection must never contain the Agent word.");
        }

        [TestMethod]
        public void ProjectFor_DoesNotExposeOtherPlayersRoles_WhilePlaying()
        {
            var state = BuildStartedState(out var agentId, out _, out _);

            var view = _projector.ProjectFor(state, agentId);

            // No living player's role is revealed mid-game (the per-player view has no secret
            // role field at all; RevealedRole stays null until the role is public).
            Assert.IsTrue(view.Players.All(p => p.RevealedRole is null),
                "No player's role may be revealed during the clue phase.");
        }

        [TestMethod]
        public void ProjectFor_RevealsEliminatedPlayerRole_DuringReveal()
        {
            var state = BuildStartedState(out var agentId, out _, out _);
            state.GamePlayers[agentId].IsEliminated = true;
            state.Execute(() => state.SetPhase(CodewordGamePhase.Reveal));

            var view = _projector.ProjectFor(state, Guid.NewGuid());

            var eliminated = view.Players.Single(p => p.PlayerId == agentId);
            Assert.AreEqual(Role.Agent, eliminated.RevealedRole, "An eliminated player's role becomes public at reveal.");
            Assert.IsTrue(view.Players.Where(p => p.PlayerId != agentId).All(p => p.RevealedRole is null),
                "Living players' roles stay hidden during the reveal.");
        }

        [TestMethod]
        public void ProjectFor_RevealsAllRoles_AtGameOver()
        {
            var state = BuildStartedState(out _, out _, out _);
            state.Execute(() => state.SetPhase(CodewordGamePhase.GameOver));

            var view = _projector.ProjectFor(state, Guid.NewGuid());

            Assert.IsTrue(view.Players.All(p => p.RevealedRole is not null),
                "Every player's role is revealed on the game-over scoreboard.");
        }

        [TestMethod]
        public void ProjectFor_RoundTripsThroughSourceGenContext_StringKeyedGameScores()
        {
            var state = BuildStartedState(out var agentId, out _, out _);
            state.GameScores[agentId] = 7;

            var view = _projector.ProjectFor(state, agentId);

            // Write side mirrors GameViewCoordinator (reflection, enums as strings); read side is
            // the trim-safe source-gen path the WASM client actually ships.
            var json = JsonSerializer.Serialize(view, view.GetType(), WireWriteOptions);
            var roundTripped = JsonSerializer.Deserialize(json, CodewordContractsJsonContext.Default.CodewordView);

            Assert.IsNotNull(roundTripped);
            Assert.AreEqual(CodewordGamePhase.CluePhase, roundTripped!.Phase);
            Assert.AreEqual(Role.Agent, roundTripped.MyRole);
            // The Guid-keyed score dictionary survives as a string-keyed wire dict.
            Assert.AreEqual(7, roundTripped.GameScores[agentId.ToString()]);
        }

        [TestMethod]
        public void Projector_ImplementsUntypedGameStateProjector()
        {
            var state = BuildStartedState(out _, out _, out _);

            object? untyped = ((IGameStateProjector)_projector).ProjectFor(state, Guid.NewGuid());

            Assert.IsInstanceOfType<CodewordView>(untyped);
        }
    }
}
