using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
{
    /// <summary>
    /// Outfit building phase: players simultaneously claim items from the shared pool
    /// to assemble a complete outfit.
    /// <para>
    /// Timer: when the deadline passes any incomplete outfits are auto-filled with random
    /// available items (Case 2), and Outfit-2 distinctness violations are resolved by
    /// swapping duplicate items (Case 5). The FSM then transitions automatically to
    /// <see cref="OutfitCustomizationState"/>.
    /// </para>
    /// </summary>
    public sealed class OutfitBuildingState
        : IDrawnToDressGameState,
          ITimedGameState<DrawnToDressGameContext, DrawnToDressCommand>
    {
        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            context.State.SetPhase(GamePhase.OutfitBuilding);
            context.State.SetPhaseDeadline(
                DateTimeOffset.UtcNow.AddSeconds(context.Settings.OutfitBuildingTimeLimit));
            context.Logger.LogInformation(
                "FSM → OutfitBuildingState (round {round}, deadline: {dl})",
                context.State.CurrentOutfitRound, context.State.PhaseDeadlineUtc);
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
                "OutfitBuildingState: timer expired (round {round}), auto-filling outfits.",
                context.State.CurrentOutfitRound);

            return DoEndOutfitBuilding(context);
        }

        // ── Commands ──────────────────────────────────────────────────────────

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleCommand(
            DrawnToDressGameContext context, DrawnToDressCommand command)
        {
            return command switch
            {
                ClaimItemCommand cmd => HandleClaim(context, cmd),
                ReturnItemCommand cmd => HandleReturn(context, cmd),
                LockOutfitCommand cmd => HandleLock(context, cmd),
                EndOutfitBuildingCommand cmd => HandleEnd(context, cmd),
                _ => null
            };
        }

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleClaim(
            DrawnToDressGameContext context, ClaimItemCommand cmd)
        {
            // Validate item and creator rule (no lock needed — creator never changes)
            var itemMeta = context.State.AllDrawings.FirstOrDefault(d => d.Id == cmd.ItemId);
            if (itemMeta is null)
                return new ResultError("Item not found.");

            if (itemMeta.CreatorId == cmd.PlayerId)
                return new ResultError("You cannot claim your own drawing.");

            var outfit = context.GetOrCreatePendingOutfit(cmd.PlayerId,
                context.State.Players.FirstOrDefault(p => p.Id == cmd.PlayerId)?.Name
                    ?? context.State.Host.Name);

            if (outfit.IsLocked)
                return new ResultError("Your outfit is locked and cannot be changed.");

            var existing = outfit.Items[itemMeta.Type];

            var claimed = context.State.ClaimItem(cmd.ItemId);
            if (claimed is null)
                return new ResultError("That item was just claimed by another player. Please choose another.");

            // Return the previously held item to the pool
            if (existing is not null)
                context.State.ReturnItem(existing);

            outfit.Items[itemMeta.Type] = claimed;
            return null;
        }

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleReturn(
            DrawnToDressGameContext context, ReturnItemCommand cmd)
        {
            var outfit = context.State.GetPlayerOutfit(cmd.PlayerId, context.State.CurrentOutfitRound);
            if (outfit is null || outfit.IsLocked)
                return new ResultError("Outfit not found or is already locked.");

            var item = outfit.Items[cmd.SlotType];
            if (item is null || item.Id != cmd.ItemId)
                return new ResultError("Item not in the specified slot.");

            outfit.Items[cmd.SlotType] = null;
            context.State.ReturnItem(item);
            return null;
        }

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleLock(
            DrawnToDressGameContext context, LockOutfitCommand cmd)
        {
            var outfit = context.State.GetPlayerOutfit(cmd.PlayerId, context.State.CurrentOutfitRound);
            if (outfit is null)
                return new ResultError("No outfit in progress.");

            if (!outfit.IsComplete)
                return new ResultError("Outfit must have one item of each clothing type before locking.");

            outfit.IsLocked = true;
            return null;
        }

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleEnd(
            DrawnToDressGameContext context, EndOutfitBuildingCommand cmd)
        {
            if (!context.IsHost(cmd.PlayerId))
                return new ResultError("Only the host can end the outfit building phase.");

            return DoEndOutfitBuilding(context);
        }

        /// <summary>
        /// Shared end-of-phase logic: auto-lock complete outfits, auto-fill incomplete ones,
        /// resolve distinctness violations for round 2+, then transition to customization.
        /// </summary>
        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> DoEndOutfitBuilding(
            DrawnToDressGameContext context)
        {
            // Auto-lock complete unlocked outfits
            foreach (var outfit in context.State.Outfits.Values
                .Where(o => o.OutfitNumber == context.State.CurrentOutfitRound && !o.IsLocked && o.IsComplete))
            {
                outfit.IsLocked = true;
            }

            // Auto-fill and lock any remaining incomplete outfits (timer expiry / host force-end)
            context.AutoFillAndLockIncompleteOutfits();

            // Fix distinctness violations for Outfit 2 (Case 5)
            context.FixDistinctnessViolations();

            context.Logger.LogInformation(
                "OutfitBuildingState: ended outfit building (round {round}).",
                context.State.CurrentOutfitRound);

            return new OutfitCustomizationState();
        }
    }
}
