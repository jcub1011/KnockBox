using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
{
    /// <summary>
    /// Timed display state that reveals the communal clothing pool to all players before
    /// outfit building begins. Supports multiple outfit rounds via the <c>outfitRound</c>
    /// parameter.
    ///
    /// For round 1, optionally reveals the theme (AfterDrawing mode).
    /// For round > 1, resets the pool (removes previous round picks).
    /// </summary>
    public sealed class PoolRevealState : ITimedDrawnToDressGameState
    {
        private readonly int _outfitRound;

        public PoolRevealState(int outfitRound = 1)
        {
            _outfitRound = outfitRound;
        }

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            context.State.PhaseDeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(context.Config.PoolRevealTimeSec);
            context.State.SetPhase(GamePhase.PoolReveal);
            context.CurrentOutfitRound = _outfitRound;
            context.ResetReadyFlags();

            if (_outfitRound == 1)
            {
                context.Logger.LogInformation(
                    "FSM → PoolRevealState. Pool contains {count} item(s). Deadline: {deadline}.",
                    context.ClothingPool.Count, context.State.PhaseDeadlineUtc);

                // In AfterDrawing mode the theme is revealed now that drawing is complete.
                if (context.Config.ThemeAnnouncement == ThemeAnnouncement.AfterDrawing &&
                    !context.State.ThemeRevealedToPlayers)
                {
                    context.State.ThemeRevealedToPlayers = true;
                    context.Logger.LogInformation(
                        "AfterDrawing: theme revealed: [{id}] \"{name}\".",
                        context.State.CurrentTheme?.Id, context.State.CurrentTheme?.DisplayName);
                }
            }
            else
            {
                context.ResetPoolForRound(_outfitRound);
                context.Logger.LogInformation(
                    "FSM → PoolRevealState (round {round}). Pool has {count} item(s). Deadline: {deadline}.",
                    _outfitRound, context.ClothingPool.Values.Count(i => i.IsInPool), context.State.PhaseDeadlineUtc);
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
            => context.State.PhaseDeadlineUtc is { } deadline
                ? deadline - now
                : new ResultError("No timer active.");

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> Tick(
            DrawnToDressGameContext context, DateTimeOffset now)
        {
            if (context.State.PhaseDeadlineUtc is not { } deadline || now < deadline) return null;

            context.Logger.LogInformation(
                "Pool reveal timer expired (round {round}). Advancing to outfit building.", _outfitRound);
            return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>.FromValue(
                new OutfitBuildingState(_outfitRound));
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

                default:
                    context.Logger.LogWarning(
                        "PoolRevealState: unrecognized command [{type}] from player [{id}].",
                        command.GetType().Name, command.PlayerId);
                    return null;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleMarkReady(
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
                "Player [{id}] marked ready during pool reveal (round {round}).", cmd.PlayerId, _outfitRound);

            if (context.AllPlayersReady())
            {
                context.Logger.LogInformation(
                    "All players ready during pool reveal (round {round}). Advancing early to outfit building.",
                    _outfitRound);
                return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>.FromValue(
                    new OutfitBuildingState(_outfitRound));
            }

            return null;
        }
    }
}
