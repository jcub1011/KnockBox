using System.Text.Json;
using System.Text.Json.Serialization;
using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;
using KnockBox.AlphaChain.Services.Logic.Games.Evaluation;
using KnockBox.AlphaChain.Services.Projection;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Tests.Unit.Support;
using KnockBox.Core.Services.State.Games.Shared.Projection;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.AlphaChain.Tests.Unit.Projection
{
    /// <summary>
    /// Projection-boundary tests for Alpha Chain. The game holds <b>no hidden state</b> — every
    /// player's bay and score is public — so these assert the view is the Contracts DTO (no
    /// <c>IModifierCard</c> leaks across the wire), cards flatten to <see cref="CardView"/>s with
    /// resolved name/description/chips, and the whole view round-trips losslessly through both the
    /// hub's reflection wire format and the trim-safe source-gen context the WASM client ships.
    /// </summary>
    [TestClass]
    public class AlphaChainStateProjectorTests
    {
        // Mirrors GameViewCoordinator's write options (enums as strings).
        private static readonly JsonSerializerOptions WireWriteOptions = new()
        {
            Converters = { new JsonStringEnumConverter() }
        };

        // Mirrors KnockBox.Core.Client ProjectionJson.DefaultOptions (the browser reader).
        private static readonly JsonSerializerOptions WireReadOptions = new()
        {
            Converters = { new JsonStringEnumConverter() },
            PropertyNameCaseInsensitive = true
        };

        private readonly AlphaChainStateProjector _projector = new();

        private static async Task<(AlphaChainGameEngine Engine, AlphaChainGameState State)> StartedGameAsync()
        {
            var engine = new AlphaChainGameEngine(
                new StubWordListService("cat", "tiger"),
                new FixedRandomNumberService(),
                new EngineEvaluator(),
                new ModifierCardFactory(),
                Mock.Of<ILogger<AlphaChainGameEngine>>(),
                Mock.Of<ILogger<AlphaChainGameState>>());

            var host = UserFactory.Create("Host", Guid.NewGuid());
            var state = (AlphaChainGameState)(await engine.CreateStateAsync(host)).Value!;
            state.RegisterPlayer(UserFactory.Create("Alice", Guid.NewGuid()));
            state.RegisterPlayer(UserFactory.Create("Bob", Guid.NewGuid()));
            state.UpdateSettings(s => s with { EnableTutorials = false });
            await engine.StartAsync(host, state);
            if (state.Phase == AlphaChainGamePhase.Countdown)
                engine.Tick(state.Context!, state.SubPhaseEndTime.AddSeconds(1));
            return (engine, state);
        }

        [TestMethod]
        public async Task ProjectFor_ReturnsContractsView_WithFlattenedCards()
        {
            var (_, state) = await StartedGameAsync();
            // Deal a card into the first player's bay so the projector exercises card flattening.
            var firstId = state.TurnManager.TurnOrder[0];
            var card = new ModifierCardFactory().CreateCard(new EngineEvaluationContext(string.Empty, [], []), ModifierId.Speedracer);
            state.Execute(() => state.GamePlayers[firstId].EngineBay.Add(card));

            var view = _projector.ProjectFor(state, firstId);

            Assert.IsInstanceOfType<AlphaChainView>(view);
            var me = view.Players.Single(p => p.UserId == firstId);
            Assert.HasCount(1, me.EngineBay);
            // The card is flattened to a Contracts CardView with its resolved presentation —
            // no IModifierCard ever appears on the view (CardView is a sealed Contracts record).
            Assert.AreEqual(ModifierId.Speedracer, me.EngineBay[0].Id);
            Assert.IsFalse(string.IsNullOrWhiteSpace(me.EngineBay[0].Name));
            Assert.IsFalse(string.IsNullOrWhiteSpace(me.EngineBay[0].Description));
        }

        [TestMethod]
        public async Task ProjectFor_AtGameOver_PopulatesEngineStepDescriptions_ForHistoryTooltip()
        {
            var (engine, state) = await StartedGameAsync();
            var actor = state.TurnManager.CurrentPlayer!.Value;
            // Give the active player a card so the scored word's breakdown has a step to describe,
            // then play a word (creating a SubmissionHistory entry) and end the match.
            var card = new ModifierCardFactory().CreateCard(new EngineEvaluationContext(string.Empty, [], []), ModifierId.Vanilla);
            state.Execute(() => state.GamePlayers[actor].EngineBay.Add(card));
            await engine.SubmitWordAsync(actor, "cat", state);
            state.Execute(() => state.SetPhase(AlphaChainGamePhase.GameOver));

            // Project through the engine's factory-backed projector (the real wiring).
            var view = (AlphaChainView)((IGameStateProjector)engine).ProjectFor(state, actor)!;

            var submission = view.PlayFeed.Single(s => s.Word.Equals("cat", StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(submission.Engine, "Game-over submissions carry their engine trace.");
            var cardStep = submission.Engine!.Steps.Single(st => st.CardId == ModifierId.Vanilla);
            Assert.IsFalse(string.IsNullOrWhiteSpace(cardStep.Description),
                "The game-over history strip needs each card's rules text for the hover tooltip.");
        }

        [TestMethod]
        public async Task ProjectFor_RoundTripsThroughHubWireFormat_Losslessly()
        {
            var (_, state) = await StartedGameAsync();
            var recipient = state.TurnManager.TurnOrder[0];

            var view = _projector.ProjectFor(state, recipient);
            var json = JsonSerializer.Serialize(view, WireWriteOptions);
            var roundTripped = JsonSerializer.Deserialize<AlphaChainView>(json, WireReadOptions);

            Assert.IsNotNull(roundTripped);
            Assert.AreEqual(view.Phase, roundTripped!.Phase);
            Assert.AreEqual(view.RecipientId, roundTripped.RecipientId);
            Assert.AreEqual(view.Settings.BanMode, roundTripped.Settings.BanMode);
            Assert.AreEqual(view.Players.Count, roundTripped.Players.Count);
            Assert.AreEqual(view.Roster.Count, roundTripped.Roster.Count);
            // Enums survive as names through the string-enum converter.
            Assert.AreEqual(view.IntermissionPhase, roundTripped.IntermissionPhase);
            CollectionAssert.AreEqual(view.LegalBanLetters.ToList(), roundTripped.LegalBanLetters.ToList());
        }

        [TestMethod]
        public async Task ProjectFor_RoundTripsThroughSourceGenContext_AsTheWasmClientDoes()
        {
            // The browser reads each projection through SourceGenProjectionDeserializer<AlphaChainView>
            // backed by AlphaChainContractsJsonContext.Default.AlphaChainView — the trim-safe path that
            // actually ships. Pin the generated metadata against the server's reflection write.
            var (_, state) = await StartedGameAsync();
            var recipient = state.TurnManager.TurnOrder[0];

            var view = _projector.ProjectFor(state, recipient);
            var json = JsonSerializer.Serialize(view, view.GetType(), WireWriteOptions);
            var roundTripped = JsonSerializer.Deserialize(
                json, AlphaChainContractsJsonContext.Default.AlphaChainView);

            Assert.IsNotNull(roundTripped);
            Assert.AreEqual(view.Phase, roundTripped!.Phase);
            Assert.AreEqual(view.Players.Count, roundTripped.Players.Count);
            Assert.AreEqual(view.MinPlayerCount, roundTripped.MinPlayerCount);
        }

        [TestMethod]
        public async Task ProjectFor_Implements_UntypedGameStateProjector()
        {
            var (_, state) = await StartedGameAsync();

            object? untyped = ((IGameStateProjector)_projector).ProjectFor(state, state.Host.Id);

            Assert.IsInstanceOfType<AlphaChainView>(untyped);
        }

        [TestMethod]
        public async Task ProjectFor_RosterReflectsRegisteredPlayers_AndRecipientHostFlag()
        {
            var (_, state) = await StartedGameAsync();

            // The host did not deal themselves in (HostPlays=false) and is not a registered player,
            // so the lobby roster (state.Players) is the two joiners — the projector mirrors that.
            var view = _projector.ProjectFor(state, state.Host.Id);

            Assert.IsTrue(view.RecipientIsHost);
            Assert.IsFalse(view.RecipientIsParticipant, "A display-only host is not an in-game player.");
            Assert.HasCount(2, view.Roster);
            CollectionAssert.AreEquivalent(
                state.Players.Select(p => p.User.Id).ToList(),
                view.Roster.Select(r => r.UserId).ToList());
        }
    }
}
