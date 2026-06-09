using KnockBox.CardCounter.Services.Logic.Games.FSM;
using KnockBox.CardCounter.Services.Logic.Games.FSM.States;
using KnockBox.CardCounter.Services.State.Games;
using KnockBox.CardCounter.Services.State.Games.Data;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Games.Shared.Projection;

namespace KnockBox.CardCounter.Services.Projection
{
    /// <summary>
    /// Builds the per-recipient <see cref="CardCounterView"/>. The projection is
    /// <b>default-deny</b>: a player's hidden hand (<see cref="PlayerState.ActionHand"/>)
    /// and temporary <see cref="PlayerState.PrivateReveal"/> are copied <i>only</i> when
    /// the player being projected is the recipient; everyone else learns just the hand
    /// count. The server-only deck stacks (<c>MainDeck</c>/<c>CurrentShoe</c>/
    /// <c>DiscardPile</c>) are never projected — only their sizes.
    /// <para>
    /// Runs inside <c>AbstractGameState.WithExclusiveRead</c> (the host's
    /// <c>GameViewCoordinator</c> holds the read lock), so it observes a consistent
    /// snapshot.
    /// </para>
    /// </summary>
    public sealed class CardCounterStateProjector
        : AbstractStateProjector<CardCounterGameState, CardCounterView>
    {
        public override CardCounterView ProjectFor(CardCounterGameState state, Guid recipientId)
        {
            var roster = state.RosterIncludingHost
                .Select(e => new RosterEntryView(e.User.Id, e.DisplayName, e.User.Id == state.Host.Id))
                .ToList();

            // In-game players, in turn order when one exists (so the UI lineup is stable).
            var turnOrder = state.TurnManager.TurnOrder;
            IEnumerable<PlayerState> ordered = turnOrder.Count > 0
                ? turnOrder.Where(state.GamePlayers.ContainsKey).Select(id => state.GamePlayers[id])
                : state.GamePlayers.Values;
            var players = ordered.Select(p => ToPlayerView(p, recipientId)).ToList();

            var shoeCounts = state.ShoeCardCounts.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value);

            ComputePhaseTiming(state, out var phaseEndsAtUtc, out var phaseDurationSeconds);

            return new CardCounterView(
                Phase: state.Phase,
                HostId: state.Host.Id,
                RecipientId: recipientId,
                HostIsParticipant: state.HostIsParticipant,
                IsJoinable: state.IsJoinable,
                CurrentPlayerId: state.CurrentPlayer,
                Roster: roster,
                Players: players,
                ShoeIndex: state.ShoeIndex,
                ShoeCardCounts: shoeCounts,
                IsNewShoe: state.IsNewShoe,
                MainDeckCount: state.MainDeck.Count,
                CurrentShoeCount: state.CurrentShoe.Count,
                DiscardHistory: state.DiscardHistory.ToList(),
                LastPlayedAction: state.LastPlayedAction,
                PendingReaction: state.PendingReaction,
                FeelingLuckyTargetId: state.FeelingLuckyTargetId,
                LastDrawnCard: state.LastDrawnCard,
                IsNotMyMoneySelecting: state.IsNotMyMoneySelecting,
                PendingNotMyMoneyOperator: state.PendingNotMyMoneyOperator,
                LastOperatorResult: state.LastOperatorResult,
                LastOperatorChange: state.LastOperatorChange,
                HedgeYourBetPlayerId: state.HedgeYourBetPlayerId,
                Settings: state.Settings,
                PhaseEndsAtUtc: phaseEndsAtUtc,
                PhaseDurationSeconds: phaseDurationSeconds,
                CreatedAtUtc: state.CreatedAt,
                ForceDrawTopId: state.ForceDrawStack.Count > 0 ? state.ForceDrawStack.Peek() : null);
        }

        private static PlayerView ToPlayerView(PlayerState p, Guid recipientId)
        {
            bool isRecipient = p.PlayerId == recipientId;
            return new PlayerView(
                PlayerId: p.PlayerId,
                DisplayName: p.DisplayName,
                Balance: p.Balance,
                Pot: p.Pot.ToList(),
                PotValue: p.PotValue,
                PassesRemaining: p.PassesRemaining,
                ExtraTurns: p.ExtraTurns,
                HasSetBuyIn: p.HasSetBuyIn,
                BuyInRoll: p.BuyInRoll,
                ActiveOperator: p.ActiveOperator,
                ActionHandCount: p.ActionHand.Count,
                // Default-deny: full hand / private reveal only for their owner.
                ActionHand: isRecipient ? p.ActionHand.ToList() : null,
                PrivateReveal: isRecipient ? p.PrivateReveal?.ToList() : null);
        }

        /// <summary>
        /// Surfaces the current timed-state deadline as an absolute UTC timestamp the
        /// client renders a countdown from (replacing the server page's direct
        /// <c>GetRemainingTime</c> read), plus the phase's total duration for styling.
        /// </summary>
        private static void ComputePhaseTiming(
            CardCounterGameState state, out DateTimeOffset? endsAtUtc, out int durationSeconds)
        {
            endsAtUtc = null;
            durationSeconds = 0;

            if (!state.Settings.EnableActionTimer
                || state.Context?.Fsm?.CurrentState is not ITimedGameState<CardCounterGameContext, CardCounterCommand> timed)
                return;

            var now = DateTimeOffset.UtcNow;
            var remaining = timed.GetRemainingTime(state.Context, now);
            if (!remaining.TryGetSuccess(out var rem) || rem.TotalSeconds < 0)
                return;

            endsAtUtc = now + rem;
            durationSeconds = (int)Math.Round(GetTotalDuration(state).TotalSeconds);
        }

        private static TimeSpan GetTotalDuration(CardCounterGameState state) => state.Phase switch
        {
            GamePhase.BuyIn => TimeSpan.FromMilliseconds(state.Settings.BuyInTimeoutMs),
            GamePhase.Playing => GetPlayingPhaseTimeout(state),
            _ => TimeSpan.FromSeconds(30)
        };

        private static TimeSpan GetPlayingPhaseTimeout(CardCounterGameState state) =>
            state.Context?.Fsm?.CurrentState switch
            {
                FeelingLuckyChainState => TimeSpan.FromMilliseconds(state.Settings.FeelingLuckyChainTimeoutMs),
                MakeMyLuckState => TimeSpan.FromMilliseconds(state.Settings.MakeMyLuckTimeoutMs),
                NotMyMoneyState => TimeSpan.FromMilliseconds(state.Settings.NotMyMoneyTimeoutMs),
                SkimState => TimeSpan.FromMilliseconds(state.Settings.SkimTimeoutMs),
                WaitingForReactionState => TimeSpan.FromMilliseconds(state.Settings.WaitingForReactionTimeoutMs),
                RoundEndState => TimeSpan.FromMilliseconds(state.Settings.RoundEndTimeoutMs),
                _ => TimeSpan.FromMilliseconds(state.Settings.PlayerTurnTimeoutMs)
            };
    }
}
