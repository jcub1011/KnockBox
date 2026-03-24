using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
{
    /// <summary>
    /// Outfit customization phase: players name their outfit and optionally add a sketch overlay.
    /// Once all rounds of outfits are complete, the host advances to the voting tournament;
    /// otherwise the next outfit-building round begins.
    /// </summary>
    public sealed class OutfitCustomizationState : IDrawnToDressGameState
    {
        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            context.State.SetPhase(GamePhase.OutfitCustomization);
            context.Logger.LogInformation(
                "FSM → OutfitCustomizationState (round {round})", context.State.CurrentOutfitRound);
            return null;
        }

        public Result OnExit(DrawnToDressGameContext context) => Result.Success;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleCommand(
            DrawnToDressGameContext context, DrawnToDressCommand command)
        {
            return command switch
            {
                SubmitOutfitCommand cmd => HandleSubmitOutfit(context, cmd),
                EndCustomizationCommand cmd => HandleEndCustomization(context, cmd),
                _ => null
            };
        }

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleSubmitOutfit(
            DrawnToDressGameContext context, SubmitOutfitCommand cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd.Name))
                return new ResultError("An outfit name is required.");

            var outfit = context.State.GetPlayerOutfit(cmd.PlayerId, context.State.CurrentOutfitRound);
            if (outfit is null)
                return new ResultError("No outfit found for this player and round.");

            if (!outfit.IsComplete)
                return new ResultError("Outfit is incomplete. All clothing slots must be filled.");

            if (outfit.IsSubmitted)
                return new ResultError("Outfit has already been submitted.");

            // Distinctness check for Outfit 2+
            if (context.State.CurrentOutfitRound >= 2)
            {
                var (isDistinct, conflicting, sharedCount) = context.State.CheckDistinctnessWithDetails(outfit);
                if (!isDistinct && conflicting is not null)
                {
                    string conflictingOwner = conflicting.PlayerId == cmd.PlayerId
                        ? "your"
                        : $"{conflicting.PlayerName}'s";
                    return new ResultError(
                        $"Too similar to {conflictingOwner} first outfit ({sharedCount} matching items). " +
                        $"Swap at least {context.State.Settings.OutfitDistinctnessRule} items.");
                }
            }

            outfit.Name = cmd.Name.Trim();
            outfit.SketchData = cmd.SketchData;
            outfit.IsSubmitted = true;

            var score = context.State.GetOrAddPlayerScore(cmd.PlayerId,
                context.State.Players.FirstOrDefault(p => p.Id == cmd.PlayerId)?.Name
                    ?? context.State.Host.Name);
            if (!score.OutfitIds.Contains(outfit.Id))
                score.OutfitIds.Add(outfit.Id);

            context.Logger.LogInformation(
                "OutfitCustomizationState: player [{id}] submitted outfit '{name}'.",
                cmd.PlayerId, outfit.Name);

            // Auto-advance when all participants have submitted their outfit
            bool allSubmitted = context.AllParticipants.All(p =>
                context.State.GetPlayerOutfit(p.Id, context.State.CurrentOutfitRound)?.IsSubmitted == true);

            if (allSubmitted)
            {
                context.Logger.LogInformation(
                    "OutfitCustomizationState: all players submitted — auto-advancing.");
                return DoEndCustomization(context);
            }

            return null;
        }

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleEndCustomization(
            DrawnToDressGameContext context, EndCustomizationCommand cmd)
        {
            if (!context.IsHost(cmd.PlayerId))
                return new ResultError("Only the host can advance the game phase.");

            return DoEndCustomization(context);
        }

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> DoEndCustomization(
            DrawnToDressGameContext context)
        {
            if (context.State.CurrentOutfitRound < context.Settings.NumOutfitRounds)
            {
                // Advance to the next outfit round
                int nextRound = context.State.CurrentOutfitRound + 1;
                context.State.SetCurrentOutfitRound(nextRound);
                context.BuildAvailablePool(nextRound);
                context.Logger.LogInformation(
                    "OutfitCustomizationState: advancing to outfit round {round}.", nextRound);
                return new OutfitBuildingState();
            }

            // All outfit rounds complete → start voting tournament
            foreach (var outfit in context.State.Outfits.Values.Where(o => o.IsSubmitted))
                context.State.GetOrAddPlayerScore(outfit.PlayerId, outfit.PlayerName);

            int outfitCount = context.State.Outfits.Values.Count(o => o.IsSubmitted);
            int rounds = DrawnToDressGameContext.CalculateSwissRounds(outfitCount);
            context.State.SetTotalVotingRounds(rounds);
            context.State.AdvanceVotingRound(); // 0 → 1

            context.GenerateSwissPairings();
            context.Logger.LogInformation(
                "OutfitCustomizationState: all rounds done; starting voting ({rounds} Swiss rounds).",
                rounds);

            return new VotingState();
        }
    }
}
