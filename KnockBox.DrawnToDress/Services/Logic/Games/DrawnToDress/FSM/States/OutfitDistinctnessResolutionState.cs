using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.DrawnToDress;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
{
    /// <summary>
    /// Handles the case where two or more outfits share an item in the same clothing-type
    /// slot and <see cref="DrawnToDressConfig.RequireDistinctItemsPerSlot"/> is enabled.
    ///
    /// Each affected player must submit a <see cref="ResolveDistinctnessCommand"/> with a
    /// replacement item before voting can begin.
    ///
    /// Transition ownership:
    /// - All conflicts resolved → <see cref="VotingRoundSetupState"/>
    /// - <see cref="ResolveDistinctnessCommand"/> → resolves one conflict; may trigger advance
    /// - <see cref="PauseGameCommand"/> (host only) → <see cref="PausedState"/>
    /// - <see cref="AbandonGameCommand"/> (host only) → <see cref="AbandonedState"/>
    /// </summary>
    public sealed class OutfitDistinctnessResolutionState : IDrawnToDressGameState
    {
        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            context.State.SetPhase(GamePhase.OutfitDistinctnessResolution);
            context.ResetReadyFlags();
            context.Logger.LogInformation("FSM → OutfitDistinctnessResolutionState");
            return null;
        }

        public Result OnExit(DrawnToDressGameContext context) => Result.Success;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleCommand(
            DrawnToDressGameContext context, DrawnToDressCommand command)
        {
            switch (command)
            {
                case ResolveDistinctnessCommand cmd:
                    return HandleResolveDistinctness(context, cmd);

                case PauseGameCommand:
                    return new PausedState(this);

                case AbandonGameCommand:
                    return new AbandonedState();

                default:
                    return null;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleResolveDistinctness(
            DrawnToDressGameContext context, ResolveDistinctnessCommand cmd)
        {
            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null)
            {
                context.Logger.LogWarning(
                    "ResolveDistinctness: unknown player [{id}].", cmd.PlayerId);
                return null;
            }

            if (player.SubmittedOutfit is null)
            {
                context.Logger.LogWarning(
                    "ResolveDistinctness: player [{id}] has no submitted outfit.", cmd.PlayerId);
                return null;
            }

            if (!context.ClothingPool.TryGetValue(cmd.ReplacementItemId, out var replacement))
            {
                context.Logger.LogWarning(
                    "ResolveDistinctness: replacement item [{itemId}] not found in pool.",
                    cmd.ReplacementItemId);
                return null;
            }

            // Swap out the conflicting item in the player's outfit for the chosen replacement.
            string typeId = replacement.ClothingTypeId;
            player.SubmittedOutfit.SelectedItemsByType[typeId] = replacement.Id;
            player.IsReady = true;

            context.Logger.LogInformation(
                "Player [{id}] resolved distinctness conflict: type [{type}] → item [{itemId}].",
                cmd.PlayerId, typeId, replacement.Id);

            if (context.AllPlayersReady())
            {
                context.Logger.LogInformation("All conflicts resolved. Moving to voting setup.");
                return new VotingRoundSetupState();
            }

            return null;
        }
    }
}
