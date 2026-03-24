using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
{
    /// <summary>
    /// Drawing phase: players submit drawings for the current clothing type, and the host
    /// (or the per-sub-round countdown timer) advances through each type in sequence.
    /// Transitions to <see cref="OutfitBuildingState"/> when the last drawing type is done.
    /// </summary>
    public sealed class DrawingState
        : IDrawnToDressGameState,
          ITimedGameState<DrawnToDressGameContext, DrawnToDressCommand>
    {
        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            context.State.SetPhase(GamePhase.Drawing);
            context.State.ResetDrawingTypeIndex();
            context.State.SetPhaseDeadline(
                DateTimeOffset.UtcNow.AddSeconds(context.Settings.DrawingTimePerRound));
            context.Logger.LogInformation(
                "FSM → DrawingState (first type: {type}, deadline: {dl})",
                context.State.CurrentDrawingType, context.State.PhaseDeadlineUtc);
            return null;
        }

        public Result OnExit(DrawnToDressGameContext context)
        {
            context.State.ClearPhaseDeadline();
            return Result.Success;
        }

        // ── ITimedGameState ───────────────────────────────────────────────────

        public ValueResult<TimeSpan> GetRemainingTime(DrawnToDressGameContext context, DateTimeOffset now)
        {
            if (!context.State.PhaseDeadlineUtc.HasValue)
                return TimeSpan.Zero;
            var remaining = context.State.PhaseDeadlineUtc.Value - now;
            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> Tick(
            DrawnToDressGameContext context, DateTimeOffset now)
        {
            if (!context.State.PhaseDeadlineUtc.HasValue || now < context.State.PhaseDeadlineUtc.Value)
                return null;

            context.Logger.LogInformation(
                "DrawingState: timer expired for type {type}, auto-advancing.",
                context.State.CurrentDrawingType);

            return AdvanceRound(context);
        }

        // ── Commands ──────────────────────────────────────────────────────────

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleCommand(
            DrawnToDressGameContext context, DrawnToDressCommand command)
        {
            return command switch
            {
                SubmitDrawingCommand cmd => HandleSubmitDrawing(context, cmd),
                AdvanceDrawingRoundCommand cmd => HandleAdvance(context, cmd),
                _ => null
            };
        }

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleSubmitDrawing(
            DrawnToDressGameContext context, SubmitDrawingCommand cmd)
        {
            int existing = context.DrawingCountForPlayer(cmd.PlayerId);
            if (existing >= context.Settings.MaxItemsPerType)
                return new ResultError(
                    $"Maximum of {context.Settings.MaxItemsPerType} items per type already reached.");

            var item = new ClothingItem
            {
                CreatorId = cmd.PlayerId,
                CreatorName = context.State.Players.FirstOrDefault(p => p.Id == cmd.PlayerId)?.Name
                              ?? context.State.Host.Name,
                Type = context.State.CurrentDrawingType,
                SvgData = cmd.SvgData,
            };

            context.State.AddDrawing(item);
            context.Logger.LogInformation(
                "DrawingState: player [{id}] submitted {type} ({n}/{max}).",
                cmd.PlayerId, item.Type, existing + 1, context.Settings.MaxItemsPerType);

            return null;
        }

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleAdvance(
            DrawnToDressGameContext context, AdvanceDrawingRoundCommand cmd)
        {
            if (!context.IsHost(cmd.PlayerId))
                return new ResultError("Only the host can advance the drawing round.");

            return AdvanceRound(context);
        }

        /// <summary>
        /// Shared advance logic used by both host command and timer expiry.
        /// </summary>
        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> AdvanceRound(
            DrawnToDressGameContext context)
        {
            if (context.State.IsLastDrawingType)
            {
                // All clothing types done → start outfit 1 building
                context.Logger.LogInformation("DrawingState: all types done, transitioning to OutfitBuilding.");
                context.State.SetCurrentOutfitRound(1);
                context.BuildAvailablePool(1);
                return new OutfitBuildingState();
            }

            context.State.AdvanceDrawingType();
            // Reset the per-sub-round deadline for the new clothing type
            context.State.SetPhaseDeadline(
                DateTimeOffset.UtcNow.AddSeconds(context.Settings.DrawingTimePerRound));
            context.Logger.LogInformation(
                "DrawingState: advanced to type {type}, new deadline {dl}.",
                context.State.CurrentDrawingType, context.State.PhaseDeadlineUtc);
            return null;
        }
    }
}
