using KnockBox.DrawnToDress.Services.Logic.Games;
using KnockBox.DrawnToDress.Services.State.Games;
using KnockBox.DrawnToDress.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DrawnToDress.Pages
{
    public partial class VotingRoundResultsPhase : ComponentBase
    {
        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<VotingRoundResultsPhase> Logger { get; set; } = default!;

        [Parameter] public DrawnToDressGameState GameState { get; set; } = default!;

        protected string GetEntrantLabel(EntrantId entrantId)
        {
            var player = GameState.GamePlayers.GetValueOrDefault(entrantId.PlayerId);
            return player?.DisplayName ?? entrantId.PlayerId;
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

        protected Dictionary<EntrantId, double> CalculateRoundScores(VotingRound round)
        {
            return DrawnToDressScoringService.CalculateRoundScores(
                round,
                GameState.Config.VotingCriteria,
                GameState.Votes.Values,
                GameState.CriterionCoinFlipResults);
        }

        protected HashSet<EntrantId> GetRoundLeaders(VotingRound round)
        {
            var roundScores = CalculateRoundScores(round);
            return DrawnToDressScoringService.GetRoundLeaders(roundScores);
        }

        protected bool IsCriterionFlipped(Guid matchupId, string criterionId)
        {
            return GameState.CriterionCoinFlipResults.Any(
                f => f.MatchupId == matchupId && f.CriterionId == criterionId);
        }
    }
}

