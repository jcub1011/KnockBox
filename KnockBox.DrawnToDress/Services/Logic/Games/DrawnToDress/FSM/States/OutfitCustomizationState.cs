using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.DrawnToDress;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
{
    /// <summary>
    /// Timed phase in which players add a custom name and finalize their outfit.
    ///
    /// After customization the engine checks for outfit distinctness conflicts. If
    /// <see cref="DrawnToDressConfig.RequireDistinctItemsPerSlot"/> is enabled and two
    /// outfits share an item, the FSM moves to
    /// <see cref="OutfitDistinctnessResolutionState"/>; otherwise it proceeds directly to
    /// <see cref="VotingRoundSetupState"/>.
    ///
    /// Transition ownership:
    /// - Timer expiry → distinctness check → next state
    /// - All players submit customization early → distinctness check → next state
    /// - <see cref="SubmitCustomizationCommand"/> → recorded; may trigger early advance
    /// - <see cref="PauseGameCommand"/> (host only) → <see cref="PausedState"/>
    /// - <see cref="AbandonGameCommand"/> (host only) → <see cref="AbandonedState"/>
    /// </summary>
    public sealed class OutfitCustomizationState : ITimedDrawnToDressGameState
    {
        private DateTimeOffset _deadline;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            _deadline = DateTimeOffset.UtcNow.AddSeconds(context.Config.OutfitCustomizationTimeSec);
            context.State.PhaseDeadlineUtc = _deadline;
            context.State.SetPhase(GamePhase.OutfitCustomization);
            context.ResetReadyFlags();
            context.Logger.LogInformation(
                "FSM → OutfitCustomizationState. Deadline: {deadline}.", _deadline);
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
                case SubmitCustomizationCommand cmd:
                    return HandleSubmitCustomization(context, cmd);

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

            context.Logger.LogInformation("Customization timer expired.");
            return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>.FromValue(ChooseNextState(context));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleSubmitCustomization(
            DrawnToDressGameContext context, SubmitCustomizationCommand cmd)
        {
            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null)
            {
                context.Logger.LogWarning(
                    "SubmitCustomization: unknown player [{id}].", cmd.PlayerId);
                return null;
            }

            if (player.SubmittedOutfit is null)
            {
                context.Logger.LogWarning(
                    "SubmitCustomization: player [{id}] has no submitted outfit.", cmd.PlayerId);
                return null;
            }

            if (string.IsNullOrWhiteSpace(cmd.OutfitName))
            {
                context.Logger.LogWarning(
                    "SubmitCustomization: player [{id}] submitted with no outfit name.", cmd.PlayerId);
                return null;
            }

            if (context.Config.SketchingRequired && string.IsNullOrWhiteSpace(cmd.SketchSvgContent))
            {
                context.Logger.LogWarning(
                    "SubmitCustomization: player [{id}] submitted without a required sketch.", cmd.PlayerId);
                return null;
            }

            player.SubmittedOutfit.Customization.OutfitName = cmd.OutfitName.Trim();
            player.SubmittedOutfit.Customization.SketchSvgContent = string.IsNullOrWhiteSpace(cmd.SketchSvgContent)
                ? null
                : cmd.SketchSvgContent;
            player.IsReady = true;

            context.Logger.LogInformation(
                "Player [{id}] submitted customization. Outfit name: \"{name}\". Has sketch: {hasSketch}.",
                cmd.PlayerId, cmd.OutfitName, cmd.SketchSvgContent is not null);

            if (context.AllPlayersReady())
            {
                context.Logger.LogInformation(
                    "All players submitted customization. Advancing.");
                return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>.FromValue(ChooseNextState(context));
            }

            return null;
        }

        private static IGameState<DrawnToDressGameContext, DrawnToDressCommand> ChooseNextState(DrawnToDressGameContext context)
        {
            if (context.Config.RequireDistinctItemsPerSlot && HasDistinctnessConflict(context))
            {
                context.Logger.LogInformation(
                    "Distinctness conflict detected. Moving to resolution state.");
                return new OutfitDistinctnessResolutionState();
            }

            return new Pool2RevealState();
        }

        /// <summary>
        /// Returns <see langword="true"/> when any two outfits share the same item ID in
        /// the same clothing-type slot.
        /// </summary>
        private static bool HasDistinctnessConflict(DrawnToDressGameContext context)
        {
            var seenItems = new HashSet<(string typeId, Guid itemId)>();
            foreach (var player in context.GamePlayers.Values)
            {
                if (player.SubmittedOutfit is null) continue;
                foreach (var (typeId, itemId) in player.SubmittedOutfit.SelectedItemsByType)
                {
                    if (!seenItems.Add((typeId, itemId)))
                        return true;
                }
            }
            return false;
        }
    }
}
