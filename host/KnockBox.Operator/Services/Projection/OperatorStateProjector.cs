using System;
using System.Collections.Generic;
using System.Linq;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Games.Shared.Projection;
using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM;
using KnockBox.Operator.Services.Logic.FSM.ActionCommands;
using KnockBox.Operator.Services.State;

namespace KnockBox.Operator.Services.Projection;

/// <summary>
/// Builds the per-recipient <see cref="OperatorView"/>. The projection is
/// <b>default-deny</b>: a player's <see cref="OperatorPlayerState.Hand"/> is copied (as
/// <see cref="CardView"/>s) <i>only</i> when the player being projected is the recipient;
/// everyone else learns just the hand count. The draw <c>Deck</c> order is never
/// projected — only its size.
/// <para>
/// The recipient's own cards carry the play affordances (<c>IsPlayable</c>, valid targets,
/// pairable cards) computed here from the server rules engine, so the WASM UI can run its
/// selection state machine without re-implementing any game rules (which it could not
/// reference anyway — KB1005). Display strings are pre-rendered for the same reason.
/// </para>
/// <para>
/// Runs inside <c>AbstractGameState.WithExclusiveRead</c> (the host's
/// <c>GameViewCoordinator</c> holds the read lock), so it observes a consistent snapshot.
/// </para>
/// </summary>
public sealed class OperatorStateProjector
    : AbstractStateProjector<OperatorGameState, OperatorView>
{
    public override OperatorView ProjectFor(OperatorGameState state, Guid recipientId)
    {
        var names = state.RosterIncludingHost.ToDictionary(e => e.User.Id, e => e.DisplayName);
        string NameOf(Guid id) => names.TryGetValue(id, out var n) ? n : "Player";

        var roster = state.RosterIncludingHost
            .Select(e => new RosterEntryView(e.User.Id, e.DisplayName, e.User.Id == state.Host.Id))
            .ToList();

        bool isHost = recipientId == state.Host.Id;
        bool isParticipant = state.Participants.Any(e => e.User.Id == recipientId);
        bool isHostObserver = isHost && !state.HostIsParticipant;
        var currentPlayerId = state.TurnManager.CurrentPlayer;
        bool isMyTurn = currentPlayerId == recipientId;

        // Server-authoritative ranking (closest-to-zero leads; tiebreak by score time). Reused for
        // each player's LiveRank and the final standings so the spectator board matches GameOver.
        var rankByUser = state.GamePlayers.Values
            .OrderBy(p => Math.Abs(p.CurrentPoints))
            .ThenBy(p => p.ScoreTimestamp)
            .Select((p, i) => (p.UserId, Rank: i + 1))
            .ToDictionary(x => x.UserId, x => x.Rank);

        // In-game players, in turn order when one exists (stable UI lineup).
        var turnOrder = state.TurnManager.TurnOrder;
        IEnumerable<OperatorPlayerState> ordered = turnOrder.Count > 0
            ? turnOrder.Where(state.GamePlayers.ContainsKey).Select(id => state.GamePlayers[id])
            : state.GamePlayers.Values;

        var players = ordered.Select(p => new OperatorPlayerView(
            UserId: p.UserId,
            DisplayName: NameOf(p.UserId),
            CurrentPoints: p.CurrentPoints,
            ActiveOperator: p.ActiveOperator,
            HandCount: p.Hand.Count,
            IsAudited: p.IsAudited,
            IsBeingStolenFrom: p.IsBeingStolenFrom,
            IsDivideBroken: p.IsDivideBroken,
            IsCurrentTurn: currentPlayerId == p.UserId,
            IsReactionTarget: state.ReactionTargetPlayerIds.Contains(p.UserId),
            LiveRank: rankByUser.GetValueOrDefault(p.UserId))).ToList();

        // ── Recipient-only: own hand (with affordances during their Play turn) ──
        IReadOnlyList<CardView>? myHand = null;
        bool hasPlayedThisTurn = false;
        if (state.GamePlayers.TryGetValue(recipientId, out var me))
        {
            bool computeAffordances =
                state.Phase == OperatorGamePhase.Play && isMyTurn && state.Context is not null;
            myHand = me.Hand
                .Select(c => ToCardView(c, computeAffordances ? state.Context : null, me))
                .ToList();
            hasPlayedThisTurn = me.HasPlayedCardThisTurn;
        }

        // ── Reaction window (public table event + recipient's own options) ──────
        PendingActionView? pending = null;
        ReactionOptionsView? myReactionOptions = null;
        if (state.Phase == OperatorGamePhase.Reaction && state.PendingGameActionCommand is { } cmd)
        {
            string attackerName = NameOf(cmd.InitiatorPlayerId);
            pending = new PendingActionView(
                AttackerId: cmd.InitiatorPlayerId,
                AttackerName: attackerName,
                Card: cmd.PrimaryCard is { } pc ? ToCardView(pc, null, null) : null,
                TargetPlayerIds: state.ReactionTargetPlayerIds.ToList(),
                Description: $"{attackerName} played {cmd.PrimaryCard?.TooltipName() ?? "an action"}.");

            bool recipientIsActiveTarget =
                state.ReactionTargetPlayerIds.Contains(recipientId)
                && !state.PlayerReactions.Any(r => r.PlayerId == recipientId);
            if (recipientIsActiveTarget && state.GamePlayers.TryGetValue(recipientId, out var defender))
            {
                var shieldIds = defender.Hand.Where(c => c is ShieldCard).Select(c => c.Id).ToList();
                Guid? hotPotatoId = null;
                IReadOnlyList<Guid> redirectTargets = [];
                if (state.PendingGameActionCommand is HotPotatoCommand)
                {
                    var hp = defender.Hand.FirstOrDefault(c => c is HotPotatoCard);
                    if (hp is not null)
                    {
                        hotPotatoId = hp.Id;
                        redirectTargets = state.GamePlayers.Keys.Where(id => id != recipientId).ToList();
                    }
                }
                myReactionOptions = new ReactionOptionsView(shieldIds, hotPotatoId, redirectTargets);
            }
        }

        // ── Standings (closest-to-zero wins; mirrors GameOverState ordering) ────
        var standings = state.GamePlayers.Values
            .OrderBy(p => Math.Abs(p.CurrentPoints))
            .ThenBy(p => p.ScoreTimestamp)
            .Select(p => new PlayerStandingView(
                p.UserId, NameOf(p.UserId), p.CurrentPoints, rankByUser[p.UserId], p.UserId == state.WinnerPlayerId))
            .ToList();

        return new OperatorView(
            Phase: state.Phase,
            HostId: state.Host.Id,
            RecipientId: recipientId,
            IsHost: isHost,
            IsParticipant: isParticipant,
            IsHostObserver: isHostObserver,
            IsMyTurn: isMyTurn,
            IsJoinable: state.IsJoinable,
            CurrentPlayerId: currentPlayerId,
            Roster: roster,
            Players: players,
            Settings: OperatorSettingsMapping.ToView(state.Settings),
            SetupPositivePoints: state.Settings.InitialPointsPositive,
            SetupNegativePoints: state.Settings.InitialPointsNegative,
            PhaseExpiresAtUtc: ComputePhaseExpiry(state),
            TurnCount: state.TurnCount,
            DeckCount: state.Deck.Count,
            DiscardPile: state.DiscardPile.Select(c => ToCardView(c, null, null)).ToList(),
            ActionLog: state.ActionLog
                .TakeLast(50)
                .Select(e => new ActionLogView(e.Message, e.SourcePlayerId, e.TargetPlayerId, e.Timestamp))
                .ToList(),
            MyHand: myHand,
            HasPlayedCardThisTurn: hasPlayedThisTurn,
            PendingAction: pending,
            MyReactionOptions: myReactionOptions,
            LastBlockedActionMessage: state.LastBlockedActionMessage,
            BlockedAttackerId: state.BlockedAttackerId,
            WinnerPlayerId: state.WinnerPlayerId,
            Standings: standings);
    }

    /// <summary>
    /// Renders a server <see cref="Card"/> to the wire. When <paramref name="ctx"/> and
    /// <paramref name="me"/> are non-null the play affordances are computed from the rules
    /// engine (recipient's own hand on their turn); otherwise the card is display-only
    /// (opponents' implicit count, discard pile, pending-action card) with inert affordances.
    /// </summary>
    private static CardView ToCardView(Card card, OperatorGameContext? ctx, OperatorPlayerState? me)
    {
        var op = card is OperatorCard oc ? oc.OperatorValue : CardOperator.None;
        var action = card is ActionCard ac ? ac.ActionValue : CardAction.None;
        decimal? number = card is NumberCard nc ? nc.NumberValue : null;

        bool isPlayable = false;
        IReadOnlyList<Guid> targets = [];
        IReadOnlyList<Guid> pairables = [];
        if (ctx is not null && me is not null)
        {
            isPlayable = card.IsPlayable(ctx, me);
            if (card is ITargetableCard targetable)
                targets = targetable.GetPotentialTargets(ctx, me).Select(p => p.UserId).ToList();
            if (card is IPairableCard pairable)
                pairables = pairable.GetPotentialPairingCards(ctx, me).Select(c => c.Id).ToList();
        }

        return new CardView(
            Id: card.Id,
            Type: card.Type,
            Operator: op,
            Action: action,
            NumberValue: number,
            Icon: card.CardIcon(),
            TooltipName: card.TooltipName(),
            TooltipDescription: card.TooltipDescription(),
            IsPlayable: isPlayable,
            ValidTargetPlayerIds: targets,
            PairableCardIds: pairables);
    }

    /// <summary>
    /// Surfaces the current timed FSM state's deadline as an absolute UTC timestamp the
    /// client renders a countdown from. Returns <see langword="null"/> when the phase is
    /// untimed or timers are disabled (the timed states return <see cref="TimeSpan.MaxValue"/>
    /// in that case, which the sub-day guard filters out).
    /// </summary>
    private static DateTimeOffset? ComputePhaseExpiry(OperatorGameState state)
    {
        if (state.Context?.Fsm?.CurrentState is not ITimedGameState<OperatorGameContext, OperatorCommand> timed)
            return null;

        var now = DateTimeOffset.UtcNow;
        if (!timed.GetRemainingTime(state.Context, now).TryGetSuccess(out var remaining))
            return null;
        if (remaining < TimeSpan.Zero || remaining >= TimeSpan.FromDays(1))
            return null;

        return now + remaining;
    }
}
