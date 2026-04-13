using KnockBox.Core.Services.Drawing;
using KnockBox.Services.Logic.Games.DrawnToDress;
using KnockBox.Services.Logic.Games.DrawnToDress.FSM;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;
using KnockBox.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DrawnToDress.Pages
{
    public partial class VotingPhase : ComponentBase
    {
        [Inject] protected DrawnToDressGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<VotingPhase> Logger { get; set; } = default!;

        [Parameter] public DrawnToDressGameState GameState { get; set; } = default!;

        private readonly Dictionary<(Guid matchupId, string criterionId), EntrantId> _selectedVotes = new();
        private bool _submitting;
        private string? _errorMessage;

        private string CurrentPlayerId => UserService.CurrentUser?.Id ?? string.Empty;

        protected bool IsCreatorOfEntrant(EntrantId entrantId)
        {
            return entrantId.PlayerId == CurrentPlayerId;
        }

        protected bool IsCompetingInMatchup(SwissMatchup matchup)
        {
            return IsCreatorOfEntrant(matchup.EntrantAId) || IsCreatorOfEntrant(matchup.EntrantBId);
        }

        protected string GetEntrantDisplayName(EntrantId entrantId)
        {
            var player = GameState.GamePlayers.GetValueOrDefault(entrantId.PlayerId);
            string name = player?.DisplayName ?? entrantId.PlayerId;
            return $"{name} (Outfit {entrantId.Round})";
        }

        /// <summary>
        /// Returns the display label for an entrant during voting.
        /// Always shows the outfit name. Optionally shows creator name below
        /// when <see cref="DrawnToDressConfig.ShowCreatorDuringVoting"/> is enabled.
        /// </summary>
        protected string GetEntrantLabel(EntrantId entrantId)
        {
            var outfit = GetEntrantOutfit(entrantId);
            string outfitName = outfit?.Customization.OutfitName ?? "Unnamed Outfit";

            if (GameState.Config.ShowCreatorDuringVoting)
            {
                string creatorName = GetEntrantDisplayName(entrantId);
                return $"{outfitName} — {creatorName}";
            }

            return outfitName;
        }

        protected OutfitSubmission? GetEntrantOutfit(EntrantId entrantId)
        {
            return GameState.Context?.GetOutfitByEntrantId(entrantId);
        }

        protected void SelectVote(Guid matchupId, string criterionId, EntrantId entrantId)
        {
            _selectedVotes[(matchupId, criterionId)] = entrantId;
            StateHasChanged();
        }

        /// <summary>
        /// Loads previously submitted votes for a matchup into the local selection dictionary
        /// so players can see and change their existing votes.
        /// </summary>
        protected void LoadExistingVotes(Guid matchupId)
        {
            var existingVotes = GameState.Votes.Values
                .Where(v => v.VoterPlayerId == CurrentPlayerId && v.MatchupId == matchupId);

            foreach (var vote in existingVotes)
            {
                _selectedVotes[(matchupId, vote.CriterionId)] = vote.ChosenEntrantId;
            }
        }

        protected bool AllCriteriaVotedForMatchup(Guid matchupId)
        {
            return GameState.Config.VotingCriteria.All(c =>
                _selectedVotes.ContainsKey((matchupId, c.Id)));
        }

        protected bool HasAlreadySubmittedVotesForMatchup(Guid matchupId)
        {
            return GameState.Votes.Values.Any(v =>
                v.VoterPlayerId == CurrentPlayerId && v.MatchupId == matchupId);
        }

        protected async Task SubmitVotesForMatchupAsync(Guid matchupId)
        {
            if (GameState.Context is null) return;

            _errorMessage = null;
            _submitting = true;
            StateHasChanged();

            try
            {
                foreach (var criterion in GameState.Config.VotingCriteria)
                {
                    var key = (matchupId, criterion.Id);
                    if (!_selectedVotes.TryGetValue(key, out var chosenEntrantId)) continue;

                    var cmd = new CastVoteCommand(CurrentPlayerId, matchupId, criterion.Id, chosenEntrantId);
                    var result = GameEngine.ProcessCommand(GameState.Context, cmd);
                    if (result.TryGetFailure(out var err))
                    {
                        _errorMessage = err.PublicMessage;
                        Logger.LogWarning("CastVote failed: {msg}", err.PublicMessage);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected error submitting votes.");
                _errorMessage = "An unexpected error occurred. Please try again.";
            }
            finally
            {
                _submitting = false;
                StateHasChanged();
            }
        }

        protected int TotalOutfitWidth =>
            CompositeCanvasLayout.ComputeCompositeWidth(GameState.Config.ClothingTypes);

        protected int TotalOutfitHeight =>
            CompositeCanvasLayout.ComputeCompositeHeight(GameState.Config.ClothingTypes);

        protected static string? SafeSvgContent(string? raw)
            => SvgContentSanitizer.Sanitize(raw);
    }
}

