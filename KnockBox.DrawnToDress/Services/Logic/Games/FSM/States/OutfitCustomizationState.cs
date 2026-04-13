using KnockBox.DrawnToDress.Services.Logic.Games;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Extensions.Returns;
using KnockBox.DrawnToDress.Services.State.Games;
using KnockBox.DrawnToDress.Services.State.Games.Data;

namespace KnockBox.DrawnToDress.Services.Logic.Games.FSM.States
{
    /// <summary>
    /// Timed phase in which players add a custom name and finalize their outfit.
    /// Supports multiple outfit rounds via the <c>outfitRound</c> parameter.
    ///
    /// After round 1 customization, the engine checks for outfit distinctness conflicts.
    /// If conflicts exist, moves to <see cref="OutfitDistinctnessResolutionState"/>;
    /// otherwise proceeds to the next round's pool reveal or voting.
    /// </summary>
    public sealed class OutfitCustomizationState : ITimedDrawnToDressGameState
    {
        private readonly int _outfitRound;

        public OutfitCustomizationState(int outfitRound = 1)
        {
            _outfitRound = outfitRound;
        }

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            if (context.Config.EnableTimer)
            {
                context.State.PhaseDeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(context.Config.OutfitCustomizationTimeSec);
            }

            context.State.SetPhase(GamePhase.OutfitCustomization);
            context.CurrentOutfitRound = _outfitRound;
            context.ResetReadyFlags();
            context.Logger.LogInformation(
                "FSM → OutfitCustomizationState (round {round}). Deadline: {deadline}.", _outfitRound, context.State.PhaseDeadlineUtc);
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

                case UpdateDraftOutfitNameCommand cmd:
                    return HandleUpdateDraftOutfitName(context, cmd);

                case PauseGameCommand:
                    return new PausedState(this);

                default:
                    context.Logger.LogWarning(
                        "OutfitCustomizationState: unrecognized command [{type}] from player [{id}].",
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

            context.Logger.LogInformation("Customization timer expired (round {round}).", _outfitRound);
            return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>.FromValue(ChooseNextState(context));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleSubmitCustomization(
            DrawnToDressGameContext context, SubmitCustomizationCommand cmd)
        {
            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null)
            {
                context.Logger.LogWarning(
                    "SubmitCustomization: unknown player [{id}].", cmd.PlayerId);
                return null;
            }

            var outfit = player.GetOutfit(_outfitRound);
            if (outfit is null)
            {
                context.Logger.LogWarning(
                    "SubmitCustomization: player [{id}] has no submitted outfit for round {round}.",
                    cmd.PlayerId, _outfitRound);
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

            outfit.Customization.OutfitName = cmd.OutfitName.Trim();
            outfit.Customization.SketchSvgContent = string.IsNullOrWhiteSpace(cmd.SketchSvgContent)
                ? null
                : cmd.SketchSvgContent;

            if (cmd.ItemPositionOverrides is { Count: > 0 })
            {
                int canvasWidth = CompositeCanvasLayout.ComputeCompositeWidth(context.Config.ClothingTypes);
                int totalHeight = CompositeCanvasLayout.ComputeCompositeHeight(context.Config.ClothingTypes);

                var clothingTypeById = context.Config.ClothingTypes.ToDictionary(ct => ct.Id);

                foreach (var kvp in cmd.ItemPositionOverrides)
                {
                    if (!clothingTypeById.TryGetValue(kvp.Key, out var ct))
                    {
                        context.Logger.LogWarning(
                            "SubmitCustomization: player [{id}] submitted position override for unknown clothing type \"{typeId}\". Skipping.",
                            cmd.PlayerId, kvp.Key);
                        continue;
                    }

                    // Allow up to 50% of the item off each edge, matching the client drag/input bounds.
                    kvp.Value.X = Math.Clamp(kvp.Value.X, -ct.CanvasWidth / 2.0, canvasWidth - ct.CanvasWidth / 2.0);
                    kvp.Value.Y = Math.Clamp(kvp.Value.Y, -ct.CanvasHeight / 2.0, totalHeight - ct.CanvasHeight / 2.0);
                }

                outfit.Customization.ItemPositionOverrides = cmd.ItemPositionOverrides;
            }

            player.IsReady = true;

            context.Logger.LogInformation(
                "Player [{id}] submitted customization (round {round}). Outfit name: \"{name}\". Has sketch: {hasSketch}.",
                cmd.PlayerId, _outfitRound, cmd.OutfitName, cmd.SketchSvgContent is not null);

            if (context.AllPlayersReady())
            {
                context.Logger.LogInformation(
                    "All players submitted customization (round {round}). Advancing.", _outfitRound);
                return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>.FromValue(ChooseNextState(context));
            }

            return null;
        }

        private ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleUpdateDraftOutfitName(
            DrawnToDressGameContext context, UpdateDraftOutfitNameCommand cmd)
        {
            var player = context.GetPlayer(cmd.PlayerId);
            if (player is not null)
            {
                player.DraftOutfitName = cmd.DraftName;
                context.State.StateChangedEventManager.Notify();
            }
            return null;
        }

        private IGameState<DrawnToDressGameContext, DrawnToDressCommand> ChooseNextState(DrawnToDressGameContext context)
        {
            // Apply draft names for any player who hasn't manually submitted yet,
            // then clear the draft so it cannot leak into subsequent outfit rounds.
            foreach (var player in context.GamePlayers.Values)
            {
                if (!player.IsReady)
                {
                    var outfit = player.GetOutfit(_outfitRound);
                    if (outfit is not null && !string.IsNullOrWhiteSpace(player.DraftOutfitName))
                    {
                        outfit.Customization.OutfitName = player.DraftOutfitName.Trim();
                        context.Logger.LogInformation(
                            "Applying draft name \"{name}\" for player [{id}] (timer expired).",
                            outfit.Customization.OutfitName, player.PlayerId);
                    }
                }

                // Clear any draft name at the end of this round to avoid reusing it in later rounds.
                player.DraftOutfitName = string.Empty;
            }

            if (_outfitRound < context.Config.NumOutfitRounds)
            {
                // More outfit rounds to go — check distinctness, then proceed to next round.
                if (_outfitRound == 1 && context.Config.RequireDistinctItemsPerSlot && HasDistinctnessConflict(context))
                {
                    context.Logger.LogInformation(
                        "Distinctness conflict detected. Moving to resolution state.");
                    return new OutfitDistinctnessResolutionState();
                }

                return new PoolRevealState(_outfitRound + 1);
            }

            // Last outfit round — proceed to voting.
            return new VotingRoundSetupState();
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
