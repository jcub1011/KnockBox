using KnockBox.Services.Logic.Games.DrawnToDress;
using KnockBox.Services.Logic.Games.DrawnToDress.FSM;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;
using KnockBox.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Components.Pages.Games.DrawnToDress
{
    public partial class VotingRoundResultsPhase : ComponentBase
    {
        [Inject] protected DrawnToDressGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<VotingRoundResultsPhase> Logger { get; set; } = default!;

        [Parameter] public DrawnToDressGameState GameState { get; set; } = default!;

        private bool _submitting;
        private string? _errorMessage;

        private string CurrentPlayerId => UserService.CurrentUser?.Id ?? string.Empty;

        protected string GetEntrantLabel(string entrantId)
        {
            var playerId = DrawnToDressGameContext.GetPlayerIdFromEntrantId(entrantId);
            var player = GameState.GamePlayers.GetValueOrDefault(playerId);
            return player?.DisplayName ?? playerId;
        }

        protected (double AScore, double BScore) CalculateCriterionScores(SwissMatchup matchup, VotingCriterionDefinition criterion)
        {
            return DrawnToDressScoringService.CalculateCriterionScores(
                matchup, criterion.Id, criterion.Weight, GameState.Votes.Values);
        }

        protected (double ATotal, double BTotal) CalculateMatchupTotals(SwissMatchup matchup)
        {
            return DrawnToDressScoringService.CalculateMatchupTotals(
                matchup,
                GameState.Config.VotingCriteria,
                GameState.Votes.Values,
                GameState.CriterionCoinFlipResults);
        }

        protected Dictionary<string, double> CalculateRoundScores(VotingRound round)
        {
            return DrawnToDressScoringService.CalculateRoundScores(
                round,
                GameState.Config.VotingCriteria,
                GameState.Votes.Values,
                GameState.CriterionCoinFlipResults);
        }

        protected HashSet<string> GetRoundLeaders(VotingRound round)
        {
            var roundScores = CalculateRoundScores(round);
            return DrawnToDressScoringService.GetRoundLeaders(roundScores);
        }

        protected bool IsCriterionFlipped(Guid matchupId, string criterionId)
        {
            return GameState.CriterionCoinFlipResults.Any(
                f => f.MatchupId == matchupId && f.CriterionId == criterionId);
        }

        protected async Task MarkReadyAsync()
        {
            if (GameState.Context is null) return;

            _errorMessage = null;
            _submitting = true;
            StateHasChanged();

            try
            {
                var cmd = new MarkReadyCommand(CurrentPlayerId);
                var result = GameEngine.ProcessCommand(GameState.Context, cmd);
                if (result.TryGetFailure(out var err))
                {
                    _errorMessage = err.PublicMessage;
                    Logger.LogWarning("MarkReady failed: {msg}", err.PublicMessage);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected error marking ready.");
                _errorMessage = "An unexpected error occurred.";
            }
            finally
            {
                _submitting = false;
                StateHasChanged();
            }
        }
    }
}
