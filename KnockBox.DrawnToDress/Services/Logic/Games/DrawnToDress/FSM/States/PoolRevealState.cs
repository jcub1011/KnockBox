using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
{
    /// <summary>
    /// Timed display state that reveals the communal clothing pool to all players before
    /// outfit building begins. The pool is already populated by the end of the drawing round.
    ///
    /// Players may browse the pool and press Ready to signal they want to advance early.
    /// If all players are ready the state transitions immediately; otherwise it auto-advances
    /// when <see cref="DrawnToDressConfig.PoolRevealTimeSec"/> expires.
    ///
    /// Claiming items is explicitly rejected during this phase (view-only).
    ///
    /// When <see cref="ThemeAnnouncement.AfterDrawing"/> is configured the theme is also
    /// revealed here (i.e. <see cref="DrawnToDressGameState.ThemeRevealedToPlayers"/> is set
    /// to <see langword="true"/>).
    ///
    /// Transition ownership:
    /// - Timer expiry → <see cref="OutfitBuildingState"/>
    /// - All players mark ready early → <see cref="OutfitBuildingState"/>
    /// - <see cref="ClaimPoolItemCommand"/> → rejected (view-only phase)
    /// - <see cref="MarkReadyCommand"/> → tracked; may trigger early advance
    /// - <see cref="PauseGameCommand"/> (host only) → <see cref="PausedState"/>
    /// - <see cref="AbandonGameCommand"/> (host only) → <see cref="AbandonedState"/>
    /// </summary>
    public sealed class PoolRevealState : ITimedDrawnToDressGameState
    {
        private DateTimeOffset _deadline;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            _deadline = DateTimeOffset.UtcNow.AddSeconds(context.Config.PoolRevealTimeSec);
            context.State.PhaseDeadlineUtc = _deadline;
            context.State.SetPhase(GamePhase.PoolReveal);
            context.ResetReadyFlags();
            context.Logger.LogInformation(
                "FSM → PoolRevealState. Pool contains {count} item(s). Deadline: {deadline}.",
                context.ClothingPool.Count, _deadline);

            // In AfterDrawing mode the theme is revealed now that drawing is complete.
            if (context.Config.ThemeAnnouncement == ThemeAnnouncement.AfterDrawing &&
                !context.State.ThemeRevealedToPlayers)
            {
                context.State.ThemeRevealedToPlayers = true;
                context.Logger.LogInformation(
                    "AfterDrawing: theme revealed: [{id}] \"{name}\".",
                    context.State.CurrentTheme?.Id, context.State.CurrentTheme?.DisplayName);
            }

            return null;
        }

        public Result OnExit(DrawnToDressGameContext context)
        {
            context.State.PhaseDeadlineUtc = null;
            return Result.Success;
        }

        public ValueResult<TimeSpan> GetRemainingTime(
            DrawnToDressGameContext context, DateTimeOffset now)
            => _deadline - now;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> Tick(
            DrawnToDressGameContext context, DateTimeOffset now)
        {
            if (now < _deadline) return null;

            context.Logger.LogInformation("Pool reveal timer expired. Advancing to outfit building.");
            return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>.FromValue(
                new OutfitBuildingState());
        }

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleCommand(
            DrawnToDressGameContext context, DrawnToDressCommand command)
        {
            switch (command)
            {
                case MarkReadyCommand cmd:
                    return HandleMarkReady(context, cmd);

                case ClaimPoolItemCommand:
                    context.Logger.LogWarning(
                        "ClaimPoolItem rejected: pool reveal is view-only. Claims open in outfit building.");
                    return null;

                case PauseGameCommand:
                    return new PausedState(this);

                case AbandonGameCommand:
                    return new AbandonedState();

                default:
                    return null;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleMarkReady(
            DrawnToDressGameContext context, MarkReadyCommand cmd)
        {
            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null)
            {
                context.Logger.LogWarning(
                    "MarkReady: unknown player [{id}].", cmd.PlayerId);
                return null;
            }

            player.IsReady = true;
            context.Logger.LogInformation(
                "Player [{id}] marked ready during pool reveal.", cmd.PlayerId);

            if (context.AllPlayersReady())
            {
                context.Logger.LogInformation(
                    "All players ready during pool reveal. Advancing early to outfit building.");
                return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>.FromValue(
                    new OutfitBuildingState());
            }

            return null;
        }
    }
}
