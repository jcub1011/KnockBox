using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
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
    /// - <see cref="AbandonGameCommand"/> → <see cref="AbandonedState"/>
    /// </summary>
    public sealed class CoinFlipState(IDrawnToDressGameState returnState) : ITimedDrawnToDressGameState
    {
        private readonly IDrawnToDressGameState _returnState = returnState;
        private DateTimeOffset _deadline;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            context.State.SetPhase(GamePhase.CoinFlip);
            context.Logger.LogInformation(
                "FSM → CoinFlipState. {count} pending coin flips. Return state: {returnState}.",
                context.State.PendingCoinFlipQueue.Count, _returnState.GetType().Name);

            if (context.State.PendingCoinFlipQueue.Count == 0)
            {
                context.Logger.LogInformation("No pending coin flips. Chaining to return state.");
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

                case AbandonGameCommand:
                    return new AbandonedState();

                default:
                    context.Logger.LogWarning(
                        "CoinFlipState: unrecognized command [{type}] from player [{id}].",
                        command.GetType().Name, command.PlayerId);
                    return null;
            }
        }

        public ValueResult<TimeSpan> GetRemainingTime(
            DrawnToDressGameContext context, DateTimeOffset now)
            => _deadline - now;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> Tick(
            DrawnToDressGameContext context, DateTimeOffset now)
        {
            if (now < _deadline) return null;

            var flip = GetCurrentFlip(context);
            if (flip is null || flip.IsResolved) return null;

            // Auto-resolve: random choice on timeout.
            bool autoChoice = context.Random.GetRandomInt(2) == 0;
            flip.IsAutoResolved = true;
            context.Logger.LogInformation(
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
                string callerEntrantId = flip.EntrantAId;
                string opponentEntrantId = flip.EntrantBId;

                var playerAId = DrawnToDressGameContext.GetPlayerIdFromEntrantId(flip.EntrantAId);
                var playerBId = DrawnToDressGameContext.GetPlayerIdFromEntrantId(flip.EntrantBId);

                if (callerPlayerId == playerBId)
                {
                    callerEntrantId = flip.EntrantBId;
                    opponentEntrantId = flip.EntrantAId;
                }

                string winnerEntrantId = callerWins ? callerEntrantId : opponentEntrantId;
                flip.WinnerEntrantId = winnerEntrantId;
                flip.WinnerPlayerId = DrawnToDressGameContext.GetPlayerIdFromEntrantId(winnerEntrantId);

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

            context.Logger.LogInformation(
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
                context.Logger.LogInformation("All coin flips resolved. Transitioning to return state.");
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
                playerA = DrawnToDressGameContext.GetPlayerIdFromEntrantId(flip.EntrantAId);
                playerB = DrawnToDressGameContext.GetPlayerIdFromEntrantId(flip.EntrantBId);
            }
            else
            {
                playerA = flip.PlayerAId;
                playerB = flip.PlayerBId;
            }

            flip.CallerPlayerId = context.Random.GetRandomInt(2) == 0 ? playerA : playerB;

            _deadline = DateTimeOffset.UtcNow.AddSeconds(context.Config.CoinFlipTimeSec);
            context.State.PhaseDeadlineUtc = _deadline;

            context.Logger.LogInformation(
                "Coin flip {index} of {total}: caller is [{caller}]. Deadline: {deadline}.",
                context.State.CurrentCoinFlipIndex + 1,
                context.State.PendingCoinFlipQueue.Count,
                flip.CallerPlayerId,
                _deadline);
        }

        private static PendingCoinFlipEntry? GetCurrentFlip(DrawnToDressGameContext context)
        {
            int idx = context.State.CurrentCoinFlipIndex;
            if (idx < 0 || idx >= context.State.PendingCoinFlipQueue.Count) return null;
            return context.State.PendingCoinFlipQueue[idx];
        }
    }
}
