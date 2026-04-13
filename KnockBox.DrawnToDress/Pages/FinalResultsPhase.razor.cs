using KnockBox.DrawnToDress.Services.Logic.Games;
using KnockBox.DrawnToDress.Services.Logic.Games.FSM;
using KnockBox.Core.Services.Navigation;
using KnockBox.DrawnToDress.Services.State.Games;
using KnockBox.DrawnToDress.Services.State.Games.Data;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using Microsoft.AspNetCore.Components;
namespace KnockBox.DrawnToDress.Pages
{
    public partial class FinalResultsPhase : ComponentBase
    {
        [Inject] protected DrawnToDressGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected INavigationService NavigationService { get; set; } = default!;

        [Inject] protected IGameSessionService GameSessionService { get; set; } = default!;

        [Inject] protected ILogger<FinalResultsPhase> Logger { get; set; } = default!;

        [Parameter] public DrawnToDressGameState GameState { get; set; } = default!;

        private bool _submitting;
        private string? _errorMessage;
        private readonly HashSet<string> _expandedPlayers = new();

        private string CurrentPlayerId => UserService.CurrentUser?.Id ?? string.Empty;

        /// <summary>Per-outfit score for a single voting round.</summary>
        protected record OutfitRoundScore(int RoundNumber, EntrantId EntrantId, double Score);

        /// <summary>Full breakdown for a player: per-round outfit scores, bonus points, and bye rounds.</summary>
        protected record PlayerBreakdown(List<OutfitRoundScore> OutfitScores, int BonusPoints, List<int> ByeRounds);

        protected void ToggleBreakdown(string playerId)
        {
            if (!_expandedPlayers.Remove(playerId))
                _expandedPlayers.Add(playerId);
        }

        protected bool IsExpanded(string playerId) => _expandedPlayers.Contains(playerId);

        protected PlayerBreakdown GetBreakdown(string playerId)
        {
            var outfitScores = new List<OutfitRoundScore>();
            var votes = GameState.Votes.Values.ToList();
            var criteria = GameState.Config.VotingCriteria;
            var coinFlipResults = GameState.CriterionCoinFlipResults;

            foreach (var round in GameState.VotingRounds)
            {
                var roundScores = DrawnToDressScoringService.CalculateRoundScores(
                    round, criteria, votes, coinFlipResults);

                foreach (var (entrantId, score) in roundScores)
                {
                    if (entrantId.PlayerId == playerId)
                    {
                        outfitScores.Add(new OutfitRoundScore(round.RoundNumber, entrantId, score));
                    }
                }
            }

            // Collect bye rounds for this player.
            var byeRounds = new List<int>();
            foreach (var round in GameState.VotingRounds)
            {
                if (round.Byes.Any(b => b.PlayerId == playerId))
                    byeRounds.Add(round.RoundNumber);
            }

            var player = GameState.GamePlayers.GetValueOrDefault(playerId);
            int bonus = player?.BonusPoints ?? 0;

            return new PlayerBreakdown(outfitScores, bonus, byeRounds);
        }

        protected async Task PlayAgainAsync()
        {
            if (GameState.Context is null) return;

            _errorMessage = null;
            _submitting = true;
            StateHasChanged();

            try
            {
                var cmd = new PlayAgainCommand(CurrentPlayerId);
                var result = GameEngine.ProcessCommand(GameState.Context, cmd);
                if (result.TryGetFailure(out var err))
                {
                    _errorMessage = err.PublicMessage;
                    Logger.LogWarning("PlayAgain failed: {msg}", err.PublicMessage);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected error requesting play again.");
                _errorMessage = "An unexpected error occurred.";
            }
            finally
            {
                _submitting = false;
                StateHasChanged();
            }
        }

        protected async Task ReturnToMenuAsync()
        {
            if (GameState.Context is null) return;

            _errorMessage = null;
            _submitting = true;
            StateHasChanged();

            try
            {
                var cmd = new ReturnToMenuCommand(CurrentPlayerId);
                var result = GameEngine.ProcessCommand(GameState.Context, cmd);
                if (result.TryGetFailure(out var err))
                {
                    _errorMessage = err.PublicMessage;
                    Logger.LogWarning("ReturnToMenu failed: {msg}", err.PublicMessage);
                }
                else
                {
                    GameSessionService.LeaveCurrentSession(navigateHome: true);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected error returning to menu.");
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

