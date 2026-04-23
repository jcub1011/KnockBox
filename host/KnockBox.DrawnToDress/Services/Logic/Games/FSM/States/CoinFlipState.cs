using KnockBox.DrawnToDress.Services.Logic.Games;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Primitives.Returns;
using KnockBox.DrawnToDress.Services.State.Games;
using KnockBox.DrawnToDress.Services.State.Games.Data;

namespace KnockBox.DrawnToDress.Services.Logic.Games.FSM.States
{
    /// <summary>
    /// Interactive timed state that resolves tied criteria or final standings via coin flips.
    ///
    /// Processes entries in <see cref="DrawnToDressGameState.PendingCoinFlipQueue"/> one at a time.
    /// For each flip, a caller is randomly selected from the two affected players and given
    /// <see cref="DrawnToDressConfig.CoinFlipTimeSec"/> seconds to choose heads or tails.
    /// If the timer expires, the choice is made randomly.
    ///
    /// Transition ownership:
    /// - Empty queue on entry → chains to <paramref name="returnState"/> immediately
    /// - All flips resolved → persists results and chains to <paramref name="returnState"/>
    /// - <see cref="CoinFlipCallCommand"/> → resolves current flip, advances
    /// - Timer expiry → auto-resolves current flip, advances
    /// - <see cref="PauseGameCommand"/> (host only) → <see cref="PausedState"/>
    /// </summary>
    public sealed class CoinFlipState(IDrawnToDressGameState returnState) : ITimedDrawnToDressGameState
    {
        private readonly IDrawnToDressGameState _returnState = returnState;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            context.State.SetPhase(GamePhase.CoinFlip);
            context.Logger.LogDebug(
                "FSM → CoinFlipState. {count} pending coin flips. Return state: {returnState}.",
                context.State.PendingCoinFlipQueue.Count, _returnState.GetType().Name);

            if (context.State.PendingCoinFlipQueue.Count == 0)
            {
                context.Logger.LogDebug("No pending coin flips. Chaining to return state.");
                return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>
                    .FromValue(_returnState);
            }

            context.State.CurrentCoinFlipIndex = 0;
            SetupCurrentFlip(context);
            return null;
        }

        public Result OnExit(DrawnToDressGameContext context)
        {
            context.State.PhaseDeadlineUtc = null;
            context.State.PendingCoinFlipMatchupId = null;
            return Result.Success;
        }

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleCommand(
            DrawnToDressGameContext context, DrawnToDressCommand command)
        {
            switch (command)
            {
                case CoinFlipCallCommand cmd:
                    return HandleCoinFlipCall(context, cmd);

                case PauseGameCommand cmd:
                    if (cmd.PlayerId != context.State.Host.Id)
                    {
                        context.Logger.LogWarning(
                            "PauseGame rejected: player [{id}] is not the host.", cmd.PlayerId);
                        return null;
                    }
                    return new PausedState(this);

                default:
                    context.Logger.LogWarning(
                        "CoinFlipState: unrecognized command [{type}] from player [{id}].",
                        command.GetType().Name, command.PlayerId);
                    return null;
            }
        }

        public ValueResult<TimeSpan> GetRemainingTime(
            DrawnToDressGameContext context, DateTimeOffset now)
            => context.State.PhaseDeadlineUtc is { } deadline
                ? deadline - now
                : new ResultError("No timer active.");

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> Tick(
            DrawnToDressGameContext context, DateTimeOffset now)
        {
            if (context.State.PhaseDeadlineUtc is not { } deadline || now < deadline) return null;

            var flip = GetCurrentFlip(context);
            if (flip is null || flip.IsResolved) return null;

            // Auto-resolve: random choice on timeout.
            bool autoChoice = context.Random.GetRandomInt(2) == 0;
            flip.IsAutoResolved = true;
            context.Logger.LogDebug(
                "Coin flip timer expired. Auto-selecting {choice} for caller [{caller}].",
                autoChoice ? "Heads" : "Tails", flip.CallerPlayerId);

            ResolveFlip(context, flip, autoChoice);
            return AdvanceToNextFlip(context);
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleCoinFlipCall(
            DrawnToDressGameContext context, CoinFlipCallCommand cmd)
        {
            var flip = GetCurrentFlip(context);
            if (flip is null || flip.IsResolved)
            {
                context.Logger.LogWarning("CoinFlipCall: no active flip to resolve.");
                return null;
            }

            if (cmd.PlayerId != flip.CallerPlayerId)
            {
                context.Logger.LogWarning(
                    "CoinFlipCall rejected: player [{id}] is not the caller [{caller}].",
                    cmd.PlayerId, flip.CallerPlayerId);
                return null;
            }

            ResolveFlip(context, flip, cmd.ChoseHeads);
            return AdvanceToNextFlip(context);
        }

        private static void ResolveFlip(DrawnToDressGameContext context, PendingCoinFlipEntry flip, bool callerChoseHeads)
        {
            flip.CallerChoseHeads = callerChoseHeads;
            flip.ResultIsHeads = context.Random.GetRandomInt(2) == 0;

            bool callerWins = flip.CallerChoseHeads == flip.ResultIsHeads;

            if (flip.Context == CoinFlipContext.CriterionTie)
            {
                // Determine which entrant the caller represents.
                string callerPlayerId = flip.CallerPlayerId;
                var callerEntrantId = flip.EntrantAId;
                var opponentEntrantId = flip.EntrantBId;

                if (callerPlayerId == flip.EntrantBId.PlayerId)
                {
                    callerEntrantId = flip.EntrantBId;
                    opponentEntrantId = flip.EntrantAId;
                }

                var winnerEntrantId = callerWins ? callerEntrantId : opponentEntrantId;
                flip.WinnerEntrantId = winnerEntrantId;
                flip.WinnerPlayerId = winnerEntrantId.PlayerId;

                // Persist to CriterionCoinFlipResults for scoring.
                context.State.CriterionCoinFlipResults.Add(
                    new CriterionCoinFlipResult(flip.MatchupId, flip.CriterionId, winnerEntrantId));
            }
            else // FinalStandingsTie
            {
                string winnerId = callerWins
                    ? (flip.CallerPlayerId == flip.PlayerAId ? flip.PlayerAId : flip.PlayerBId)
                    : (flip.CallerPlayerId == flip.PlayerAId ? flip.PlayerBId : flip.PlayerAId);
                flip.WinnerPlayerId = winnerId;
            }

            flip.IsResolved = true;

            context.Logger.LogDebug(
                "Coin flip [{id}] resolved: caller chose {choice}, result is {result} → winner [{winner}].",
                flip.Id,
                callerChoseHeads ? "Heads" : "Tails",
                flip.ResultIsHeads ? "Heads" : "Tails",
                flip.WinnerPlayerId);
        }

        private ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> AdvanceToNextFlip(
            DrawnToDressGameContext context)
        {
            context.State.CurrentCoinFlipIndex++;

            if (context.State.CurrentCoinFlipIndex >= context.State.PendingCoinFlipQueue.Count)
            {
                context.Logger.LogDebug("All coin flips resolved. Transitioning to return state.");
                return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>
                    .FromValue(_returnState);
            }

            SetupCurrentFlip(context);
            context.State.StateChangedEventManager.Notify();
            return null;
        }

        private void SetupCurrentFlip(DrawnToDressGameContext context)
        {
            var flip = GetCurrentFlip(context)!;

            // Randomly select a caller from the two affected players.
            string playerA, playerB;
            if (flip.Context == CoinFlipContext.CriterionTie)
            {
                playerA = flip.EntrantAId.PlayerId;
                playerB = flip.EntrantBId.PlayerId;
            }
            else
            {
                playerA = flip.PlayerAId;
                playerB = flip.PlayerBId;
            }

            flip.CallerPlayerId = context.Random.GetRandomInt(2) == 0 ? playerA : playerB;

            context.State.PhaseDeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(context.Config.CoinFlipTimeSec);

            context.Logger.LogDebug(
                "Coin flip {index} of {total}: caller is [{caller}]. Deadline: {deadline}.",
                context.State.CurrentCoinFlipIndex + 1,
                context.State.PendingCoinFlipQueue.Count,
                flip.CallerPlayerId,
                context.State.PhaseDeadlineUtc);
        }

        private static PendingCoinFlipEntry? GetCurrentFlip(DrawnToDressGameContext context)
        {
            int idx = context.State.CurrentCoinFlipIndex;
            if (idx < 0 || idx >= context.State.PendingCoinFlipQueue.Count) return null;
            return context.State.PendingCoinFlipQueue[idx];
        }
    }
}
