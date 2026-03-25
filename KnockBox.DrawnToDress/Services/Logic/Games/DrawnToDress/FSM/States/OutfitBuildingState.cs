using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
{
    /// <summary>
    /// Timed phase in which players assemble their outfit by claiming items from the
    /// communal pool and selecting one per clothing type.
    ///
    /// Transition ownership:
    /// - Timer expiry → <see cref="OutfitCustomizationState"/>
    /// - All players submit their outfit early → <see cref="OutfitCustomizationState"/>
    /// - <see cref="ClaimPoolItemCommand"/> → item claimed; no transition
    /// - <see cref="SubmitOutfitCommand"/> → outfit recorded; may trigger early advance
    /// - <see cref="PauseGameCommand"/> (host only) → <see cref="PausedState"/>
    /// - <see cref="AbandonGameCommand"/> (host only) → <see cref="AbandonedState"/>
    /// </summary>
    public sealed class OutfitBuildingState : ITimedDrawnToDressGameState
    {
        private DateTimeOffset _deadline;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            _deadline = DateTimeOffset.UtcNow.AddSeconds(context.Config.OutfitBuildingTimeSec);
            context.State.PhaseDeadlineUtc = _deadline;
            context.State.SetPhase(GamePhase.OutfitBuilding);
            context.ResetReadyFlags();
            context.Logger.LogInformation(
                "FSM → OutfitBuildingState. Deadline: {deadline}.", _deadline);
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
                case ClaimPoolItemCommand cmd:
                    return HandleClaimPoolItem(context, cmd);

                case SubmitOutfitCommand cmd:
                    return HandleSubmitOutfit(context, cmd);

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

            context.Logger.LogInformation("Outfit building timer expired. Moving to customization.");
            return new OutfitCustomizationState();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleClaimPoolItem(
            DrawnToDressGameContext context, ClaimPoolItemCommand cmd)
        {
            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null)
            {
                context.Logger.LogWarning(
                    "ClaimPoolItem: unknown player [{id}].", cmd.PlayerId);
                return null;
            }

            if (!context.ClothingPool.TryGetValue(cmd.ItemId, out var item))
            {
                context.Logger.LogWarning(
                    "ClaimPoolItem: item [{itemId}] not found in pool.", cmd.ItemId);
                return null;
            }

            if (item.ClaimedByPlayerId is not null)
            {
                context.Logger.LogWarning(
                    "ClaimPoolItem: item [{itemId}] is already claimed by [{claimer}].",
                    cmd.ItemId, item.ClaimedByPlayerId);
                return null;
            }

            item.ClaimedByPlayerId = cmd.PlayerId;
            player.OwnedClothingItemIds.Add(item.Id);

            context.Logger.LogInformation(
                "Player [{id}] claimed pool item [{itemId}].", cmd.PlayerId, item.Id);
            return null;
        }

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleSubmitOutfit(
            DrawnToDressGameContext context, SubmitOutfitCommand cmd)
        {
            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null)
            {
                context.Logger.LogWarning(
                    "SubmitOutfit: unknown player [{id}].", cmd.PlayerId);
                return null;
            }

            player.SubmittedOutfit = new OutfitSubmission
            {
                PlayerId = cmd.PlayerId,
                SelectedItemsByType = new Dictionary<string, Guid>(cmd.SelectedItemsByType),
                SubmittedAt = DateTimeOffset.UtcNow,
            };

            context.Logger.LogInformation(
                "Player [{id}] submitted their outfit ({count} items).",
                cmd.PlayerId, cmd.SelectedItemsByType.Count);

            if (context.AllOutfitsSubmitted())
            {
                context.Logger.LogInformation(
                    "All outfits submitted. Moving to customization early.");
                return new OutfitCustomizationState();
            }

            return null;
        }
    }
}
