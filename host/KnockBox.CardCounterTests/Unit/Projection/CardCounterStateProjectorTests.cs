using System.Text.Json;
using System.Text.Json.Serialization;
using KnockBox.CardCounter.Contracts;
using KnockBox.CardCounter.Services.Projection;
using KnockBox.CardCounter.Services.State.Games;
using KnockBox.CardCounter.Services.State.Games.Data;
using KnockBox.Core.Services.State.Games.Shared.Projection;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.CardCounter.Tests.Unit.Projection
{
    /// <summary>
    /// Projection-boundary tests — the security headline for Card Counter. A player's
    /// hidden hand and private reveal must reach <b>only</b> their owner; everyone else
    /// sees a count, and the server-only deck stacks never cross the wire. Also verifies
    /// the view round-trips through the hub's JSON format (string-keyed shoe counts +
    /// polymorphic cards).
    /// </summary>
    [TestClass]
    public class CardCounterStateProjectorTests
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

        private readonly CardCounterStateProjector _projector = new();

        private static CardCounterGameState BuildTwoPlayerState(
            out Guid aId, out Guid bId)
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var state = new CardCounterGameState(host, Mock.Of<ILogger<CardCounterGameState>>());

            aId = Guid.NewGuid();
            bId = Guid.NewGuid();

            state.GamePlayers[aId] = new PlayerState
            {
                PlayerId = aId,
                DisplayName = "Alice",
                Balance = 10,
                ActionHand = [new ActionCard(ActionType.Skim), new ActionCard(ActionType.Burn)],
                PrivateReveal = [new NumberCard(7), new OperatorCard(Operator.Multiply)],
            };
            state.GamePlayers[bId] = new PlayerState
            {
                PlayerId = bId,
                DisplayName = "Bob",
                Balance = -5,
                ActionHand = [new ActionCard(ActionType.FeelingLucky), new ActionCard(ActionType.Tilt), new ActionCard(ActionType.Compd)],
            };

            state.Execute(() => state.SetPhase(GamePhase.Playing));
            state.TurnManager.SetTurnOrder([aId, bId]);
            state.ShoeCardCounts[CardType.Number] = 5;
            state.ShoeCardCounts[CardType.Operator] = 3;
            return state;
        }

        [TestMethod]
        public void ProjectFor_GivesRecipientOwnHand_ButOnlyCountsForOthers()
        {
            var state = BuildTwoPlayerState(out var aId, out var bId);

            var viewForA = _projector.ProjectFor(state, aId);

            var aSeenByA = viewForA.Players.Single(p => p.PlayerId == aId);
            var bSeenByA = viewForA.Players.Single(p => p.PlayerId == bId);

            // Alice sees her own full hand + private reveal.
            Assert.IsNotNull(aSeenByA.ActionHand);
            Assert.AreEqual(2, aSeenByA.ActionHand!.Count);
            Assert.IsNotNull(aSeenByA.PrivateReveal);
            Assert.AreEqual(2, aSeenByA.PrivateReveal!.Count);

            // Bob's hand is HIDDEN from Alice — only the count leaks.
            Assert.IsNull(bSeenByA.ActionHand, "Opponent's hand must never be projected.");
            Assert.IsNull(bSeenByA.PrivateReveal, "Opponent's private reveal must never be projected.");
            Assert.AreEqual(3, bSeenByA.ActionHandCount);
        }

        [TestMethod]
        public void ProjectFor_IsSymmetric_EachRecipientSeesOnlyTheirOwnHand()
        {
            var state = BuildTwoPlayerState(out var aId, out var bId);

            var viewForB = _projector.ProjectFor(state, bId);

            Assert.IsNotNull(viewForB.Players.Single(p => p.PlayerId == bId).ActionHand);
            Assert.IsNull(viewForB.Players.Single(p => p.PlayerId == aId).ActionHand);
            Assert.AreEqual(2, viewForB.Players.Single(p => p.PlayerId == aId).ActionHandCount);
        }

        [TestMethod]
        public void ProjectFor_RoundTripsThroughHubWireFormat_StringKeyedShoeCountsAndPolymorphicCards()
        {
            var state = BuildTwoPlayerState(out var aId, out _);

            var view = _projector.ProjectFor(state, aId);

            var json = JsonSerializer.Serialize(view, WireWriteOptions);
            var roundTripped = JsonSerializer.Deserialize<CardCounterView>(json, WireReadOptions);

            Assert.IsNotNull(roundTripped);
            // Enum-keyed shoe counts must survive as a string-keyed dict.
            Assert.AreEqual(5, roundTripped!.ShoeCardCounts["Number"]);
            Assert.AreEqual(3, roundTripped.ShoeCardCounts["Operator"]);

            // Polymorphic cards in the recipient's own private reveal round-trip their concrete type.
            var aliceReveal = roundTripped.Players.Single(p => p.PlayerId == aId).PrivateReveal!;
            Assert.IsInstanceOfType<NumberCard>(aliceReveal[0]);
            Assert.AreEqual(7, ((NumberCard)aliceReveal[0]).Value);
            Assert.IsInstanceOfType<OperatorCard>(aliceReveal[1]);
            Assert.AreEqual(Operator.Multiply, ((OperatorCard)aliceReveal[1]).Op);

            // Action cards survive too.
            Assert.AreEqual(ActionType.Skim, roundTripped.Players.Single(p => p.PlayerId == aId).ActionHand![0].Action);
        }

        [TestMethod]
        public void ProjectFor_RoundTripsThroughSourceGenContext_AsTheWasmClientDoes()
        {
            // The browser never runs reflection-based JSON for first-party views: GameRoot
            // reads each projection through SourceGenProjectionDeserializer<CardCounterView>
            // backed by CardCounterContractsJsonContext.Default.CardCounterView (the trim-safe
            // path that actually ships). This test mirrors that exact server-write →
            // client-read boundary so the GENERATED metadata is pinned — especially the
            // polymorphic BaseCard discriminator — not just the reflection wire shape.
            var state = BuildTwoPlayerState(out var aId, out var bId);

            var view = _projector.ProjectFor(state, aId);

            // Write side: identical to GameViewCoordinator.Serialize — reflection, enums as
            // strings, concrete runtime type (the server is not trimmed).
            var json = JsonSerializer.Serialize(view, view.GetType(), WireWriteOptions);

            // Read side: the source-gen path the WASM client ships. SourceGenProjectionDeserializer
            // is a one-line wrapper over exactly this call, so reading via the context directly
            // exercises the same generated JsonTypeInfo without a Blazor-RCL test reference.
            var roundTripped = JsonSerializer.Deserialize(
                json, CardCounterContractsJsonContext.Default.CardCounterView);

            Assert.IsNotNull(roundTripped);

            // Polymorphic BaseCard discriminator resolves through the generated metadata.
            var aliceReveal = roundTripped!.Players.Single(p => p.PlayerId == aId).PrivateReveal!;
            Assert.IsInstanceOfType<NumberCard>(aliceReveal[0]);
            Assert.AreEqual(7, ((NumberCard)aliceReveal[0]).Value);
            Assert.IsInstanceOfType<OperatorCard>(aliceReveal[1]);
            Assert.AreEqual(Operator.Multiply, ((OperatorCard)aliceReveal[1]).Op);

            // The non-polymorphic ActionCard member resolves too.
            Assert.AreEqual(
                ActionType.Skim,
                roundTripped.Players.Single(p => p.PlayerId == aId).ActionHand![0].Action);

            // String-keyed shoe counts survive the generated dictionary converter.
            Assert.AreEqual(5, roundTripped.ShoeCardCounts["Number"]);
            Assert.AreEqual(3, roundTripped.ShoeCardCounts["Operator"]);

            // The security boundary holds across the real client wire path: the recipient
            // keeps her own hand; the opponent's is hidden behind a count.
            Assert.IsNotNull(roundTripped.Players.Single(p => p.PlayerId == aId).ActionHand);
            var bobSeenByA = roundTripped.Players.Single(p => p.PlayerId == bId);
            Assert.IsNull(bobSeenByA.ActionHand, "Opponent's hand must not cross even the source-gen wire.");
            Assert.AreEqual(3, bobSeenByA.ActionHandCount);
        }

        [TestMethod]
        public void ProjectFor_DoesNotExposeDeckCards_OnlyCounts()
        {
            var state = BuildTwoPlayerState(out var aId, out _);
            state.MainDeck.Push(new NumberCard(1));
            state.MainDeck.Push(new NumberCard(2));
            state.CurrentShoe.Push(new OperatorCard(Operator.Add));

            var view = _projector.ProjectFor(state, aId);

            // The view exposes sizes, never the cards themselves (compile-time: no such field).
            Assert.AreEqual(2, view.MainDeckCount);
            Assert.AreEqual(1, view.CurrentShoeCount);
        }

        [TestMethod]
        public void Projector_ImplementsUntypedGameStateProjector()
        {
            var state = BuildTwoPlayerState(out _, out _);

            object? untyped = ((IGameStateProjector)_projector).ProjectFor(state, Guid.NewGuid());

            Assert.IsInstanceOfType<CardCounterView>(untyped);
        }
    }
}
