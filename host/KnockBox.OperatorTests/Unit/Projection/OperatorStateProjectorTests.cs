using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using KnockBox.Operator.Contracts;
using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM;
using KnockBox.Operator.Services.Projection;
using KnockBox.Operator.Services.State;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.Core.Services.State.Games.Shared.Projection;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KnockBox.Operator.Tests.Unit.Projection;

/// <summary>
/// Projection-boundary tests — the security headline for Operator. A player's hand must
/// reach <b>only</b> its owner (others see a count); the draw deck order never crosses the
/// wire; reaction options reach only the current reaction target. Also verifies the
/// recipient's own cards carry the server-computed play affordances, and that the view
/// round-trips through the WASM client's source-gen JSON path.
/// </summary>
[TestClass]
public class OperatorStateProjectorTests
{
    private static readonly JsonSerializerOptions WireWriteOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly OperatorStateProjector _projector = new();

    private static OperatorGameState BuildPlayState(out Guid p1, out Guid p2)
    {
        var host = UserFactory.Create("Host", Guid.NewGuid());
        var state = new OperatorGameState(host, NullLogger<OperatorGameState>.Instance);
        state.Execute(() => state.SetJoinable(true));

        p1 = Guid.NewGuid();
        p2 = Guid.NewGuid();
        state.RegisterPlayer(UserFactory.Create("Alice", p1));
        state.RegisterPlayer(UserFactory.Create("Bob", p2));

        state.GamePlayers[p1] = new OperatorPlayerState
        {
            UserId = p1,
            CurrentPoints = 10m,
            ActiveOperator = CardOperator.Add,
            Hand = [new NumberCard(5m), new OperatorCard(CardOperator.Add), new ShieldCard()],
        };
        state.GamePlayers[p2] = new OperatorPlayerState
        {
            UserId = p2,
            CurrentPoints = -10m,
            ActiveOperator = CardOperator.Subtract,
            Hand = [new NumberCard(3m), new HotPotatoCard()],
        };

        state.TurnManager.SetTurnOrder([p1, p2]);
        state.Phase = OperatorGamePhase.Play;
        // Context (no Fsm) is enough for affordance computation on the active player's hand.
        state.Context = new OperatorGameContext(state, Mock.Of<IRandomNumberService>());
        return state;
    }

    [TestMethod]
    public void ProjectFor_GivesRecipientOwnHand_ButOnlyCountsForOthers()
    {
        var state = BuildPlayState(out var p1, out var p2);

        var viewForP1 = _projector.ProjectFor(state, p1);

        Assert.IsNotNull(viewForP1.MyHand);
        Assert.AreEqual(3, viewForP1.MyHand!.Count);

        // The opponent surfaces as a count only — there is no per-opponent hand field at all.
        var bobSeenByAlice = viewForP1.Players.Single(p => p.UserId == p2);
        Assert.AreEqual(2, bobSeenByAlice.HandCount);
    }

    [TestMethod]
    public void ProjectFor_IsSymmetric_EachRecipientSeesOnlyTheirOwnHand()
    {
        var state = BuildPlayState(out var p1, out var p2);

        var viewForP2 = _projector.ProjectFor(state, p2);

        Assert.IsNotNull(viewForP2.MyHand);
        Assert.AreEqual(2, viewForP2.MyHand!.Count);
        Assert.AreEqual(3, viewForP2.Players.Single(p => p.UserId == p1).HandCount);
    }

    [TestMethod]
    public void ProjectFor_ComputesAffordances_ForTheActivePlayersOwnHand()
    {
        var state = BuildPlayState(out var p1, out var p2);

        var viewForP1 = _projector.ProjectFor(state, p1);

        var numberCard = viewForP1.MyHand!.Single(c => c.Type == CardType.Number);
        Assert.IsTrue(numberCard.IsPlayable, "A number card is always playable.");
        Assert.AreEqual(5m, numberCard.NumberValue);

        // An operator card can target any non-audited player (server rule, projected as affordance).
        var operatorCard = viewForP1.MyHand!.Single(c => c.Type == CardType.Operator);
        Assert.IsTrue(operatorCard.IsPlayable);
        CollectionAssert.Contains(operatorCard.ValidTargetPlayerIds.ToList(), p2);
    }

    [TestMethod]
    public void ProjectFor_DoesNotExposeDeckCards_OnlyCount()
    {
        var state = BuildPlayState(out var p1, out _);
        state.Deck.Add(new NumberCard(1m));
        state.Deck.Add(new OperatorCard(CardOperator.Multiply));

        var view = _projector.ProjectFor(state, p1);

        // The view exposes the size, never the cards (compile-time: there is no deck field).
        Assert.AreEqual(2, view.DeckCount);
    }

    [TestMethod]
    public void ProjectFor_ReactionOptions_ReachOnlyTheCurrentTarget()
    {
        var state = BuildPlayState(out var p1, out var p2);
        state.Phase = OperatorGamePhase.Reaction;
        state.ReactionTargetPlayerIds = [p2];
        // The target holds a Shield, so it should surface as a reaction option for them.
        state.GamePlayers[p2].Hand.Add(new ShieldCard());

        // A Steal by p1 targeting p2 is the pending action.
        var steal = new StealCard();
        var playCmd = new KnockBox.Operator.Services.Logic.FSM.Commands.PlayCardsCommand(p1, [steal.Id], p2);
        state.PendingGameActionCommand = steal.CreateCommand(state.Context!, playCmd, [steal]);

        var viewForTarget = _projector.ProjectFor(state, p2);
        var viewForAttacker = _projector.ProjectFor(state, p1);

        // The pending action is a public table event (both see it).
        Assert.IsNotNull(viewForTarget.PendingAction);
        Assert.IsNotNull(viewForAttacker.PendingAction);
        Assert.AreEqual(p1, viewForTarget.PendingAction!.AttackerId);

        // But only the target gets reaction options, and they list the target's shield.
        Assert.IsNotNull(viewForTarget.MyReactionOptions);
        var shieldId = state.GamePlayers[p2].Hand.OfType<ShieldCard>().Single().Id;
        CollectionAssert.Contains(viewForTarget.MyReactionOptions!.ShieldCardIds.ToList(), shieldId);

        Assert.IsNull(viewForAttacker.MyReactionOptions, "Non-targets get no reaction options.");
    }

    [TestMethod]
    public void ProjectFor_RoundTripsThroughSourceGenContext_AsTheWasmClientDoes()
    {
        var state = BuildPlayState(out var p1, out var p2);

        var view = _projector.ProjectFor(state, p1);

        // Write side mirrors GameViewCoordinator (reflection, enums as strings).
        var json = JsonSerializer.Serialize(view, view.GetType(), WireWriteOptions);
        // Read side is the trim-safe source-gen path the WASM client actually ships.
        var roundTripped = JsonSerializer.Deserialize(json, OperatorContractsJsonContext.Default.OperatorView);

        Assert.IsNotNull(roundTripped);
        Assert.AreEqual(3, roundTripped!.MyHand!.Count);

        // Enum fields on a CardView survive the generated metadata.
        var op = roundTripped.MyHand!.Single(c => c.Type == CardType.Operator);
        Assert.AreEqual(CardOperator.Add, op.Operator);

        // The security boundary holds across the real client wire path.
        Assert.AreEqual(2, roundTripped.Players.Single(p => p.UserId == p2).HandCount);
    }

    [TestMethod]
    public void ProjectFor_PopulatesParityFields_RanksSetupValuesDivideBrokenAndLogTimestamps()
    {
        var state = BuildPlayState(out var p1, out _);
        state.GamePlayers[p1].IsDivideBroken = true;
        var ts = DateTimeOffset.UtcNow.AddSeconds(-30);
        state.ActionLog.Add(new ActionLogEntry("Alice played +5.", ts, p1, null));

        var view = _projector.ProjectFor(state, p1);

        // The ± starting values are surfaced for the Setup buttons.
        Assert.AreEqual(state.Settings.InitialPointsPositive, view.SetupPositivePoints);
        Assert.AreEqual(state.Settings.InitialPointsNegative, view.SetupNegativePoints);

        // Every player gets a distinct server-assigned live rank (1..N).
        var ranks = view.Players.Select(p => p.LiveRank).OrderBy(r => r).ToList();
        CollectionAssert.AreEqual(new[] { 1, 2 }, ranks);

        // The transient divide-broken flag and the log timestamp cross the wire.
        Assert.IsTrue(view.Players.Single(p => p.UserId == p1).IsDivideBroken);
        Assert.AreEqual(ts, view.ActionLog.Single().Timestamp);
    }

    [TestMethod]
    public void Projector_ImplementsUntypedGameStateProjector()
    {
        var state = BuildPlayState(out _, out _);

        object? untyped = ((IGameStateProjector)_projector).ProjectFor(state, Guid.NewGuid());

        Assert.IsInstanceOfType<OperatorView>(untyped);
    }
}
