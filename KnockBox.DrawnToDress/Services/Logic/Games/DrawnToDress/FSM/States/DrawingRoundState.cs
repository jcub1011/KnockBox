using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
{
    /// <summary>
    /// Timed phase in which every player draws one clothing item before the clock expires.
    ///
    /// Transition ownership:
    /// - Timer expiry → <see cref="PoolRevealState"/> (all remaining drawings are auto-submitted)
    /// - All players mark ready early → <see cref="PoolRevealState"/>
    /// - <see cref="SubmitDrawingCommand"/> → stored; no transition until all ready or timer fires
    /// - <see cref="PauseGameCommand"/> (host only) → <see cref="PausedState"/>
    /// - <see cref="AbandonGameCommand"/> (host only) → <see cref="AbandonedState"/>
    /// </summary>
    public sealed class DrawingRoundState : ITimedDrawnToDressGameState
    {
        private DateTimeOffset _deadline;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            _deadline = DateTimeOffset.UtcNow.AddSeconds(context.Config.DrawingTimeSec);
            context.State.PhaseDeadlineUtc = _deadline;
            context.State.SetPhase(GamePhase.Drawing);
            context.ResetReadyFlags();
            context.Logger.LogInformation(
                "FSM → DrawingRoundState. Deadline: {deadline}.", _deadline);
            return null;
        }

        public Result OnExit(DrawnToDressGameContext context)
        {
            context.State.PhaseDeadlineUtc = null;
            return Result.Success;
        }

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleCommand(
            DrawnToDressGameContext context, DrawnToDressCommand command)
        {
            switch (command)
            {
                case SubmitDrawingCommand cmd:
                    return HandleSubmitDrawing(context, cmd);

                case MarkReadyCommand cmd:
                    return HandleMarkReady(context, cmd);

                case PauseGameCommand:
                    return new PausedState(this);

                case AbandonGameCommand:
                    return new AbandonedState();

                default:
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

            context.Logger.LogInformation("Drawing timer expired. Moving to pool reveal.");
            return new PoolRevealState();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleSubmitDrawing(
            DrawnToDressGameContext context, SubmitDrawingCommand cmd)
        {
            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null)
            {
                context.Logger.LogWarning(
                    "SubmitDrawing: unknown player [{id}].", cmd.PlayerId);
                return null;
            }

            // Validate the clothing type is configured for this session.
            bool validType = context.Config.ClothingTypes
                .Any(ct => ct.Id == cmd.ClothingTypeId);
            if (!validType)
            {
                context.Logger.LogWarning(
                    "SubmitDrawing: invalid clothing type [{type}] from player [{id}].",
                    cmd.ClothingTypeId, cmd.PlayerId);
                return null;
            }

            // Store the drawn item and place it into the pool.
            var item = new DrawnClothingItem
            {
                ClothingTypeId = cmd.ClothingTypeId,
                CreatorPlayerId = cmd.PlayerId,
                SvgContent = cmd.SvgContent,
                IsInPool = true,
            };
            context.ClothingPool[item.Id] = item;
            player.OwnedClothingItemIds.Add(item.Id);

            context.Logger.LogInformation(
                "Player [{id}] submitted a [{type}] drawing (item {itemId}).",
                cmd.PlayerId, cmd.ClothingTypeId, item.Id);

            return null; // Timer drives the transition.
        }

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
                "Player [{id}] marked ready in DrawingRoundState.", cmd.PlayerId);

            if (context.AllPlayersReady())
            {
                context.Logger.LogInformation("All players ready. Moving to pool reveal early.");
                return new PoolRevealState();
            }

            return null;
        }
    }
}
