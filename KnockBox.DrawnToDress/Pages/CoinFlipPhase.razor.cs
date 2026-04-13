using KnockBox.Services.Logic.Games.DrawnToDress;
using KnockBox.Services.Logic.Games.DrawnToDress.FSM;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;
using KnockBox.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DrawnToDress.Pages
{
    public partial class CoinFlipPhase : ComponentBase
    {
        [Inject] protected DrawnToDressGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<CoinFlipPhase> Logger { get; set; } = default!;

        [Parameter] public DrawnToDressGameState GameState { get; set; } = default!;

        private bool _submitting;
        private string? _errorMessage;

        private string CurrentPlayerId => UserService.CurrentUser?.Id ?? string.Empty;

        protected string GetEntrantDisplayName(EntrantId entrantId)
        {
            var player = GameState.GamePlayers.GetValueOrDefault(entrantId.PlayerId);
            string name = player?.DisplayName ?? entrantId.PlayerId;
            return $"{name} (Outfit {entrantId.Round})";
        }

        protected async Task CallCoinFlipAsync(bool choseHeads)
        {
            if (GameState.Context is null) return;

            _errorMessage = null;
            _submitting = true;
            StateHasChanged();

            try
            {
                var cmd = new CoinFlipCallCommand(CurrentPlayerId, choseHeads);
                var result = GameEngine.ProcessCommand(GameState.Context, cmd);
                if (result.TryGetFailure(out var err))
                {
                    _errorMessage = err.PublicMessage;
                    Logger.LogWarning("CoinFlipCall failed: {msg}", err.PublicMessage);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected error calling coin flip.");
                _errorMessage = "An unexpected error occurred. Please try again.";
            }
            finally
            {
                _submitting = false;
                StateHasChanged();
            }
        }

        protected static string GetOrdinal(int rank)
        {
            if (rank <= 0) return rank.ToString();
            int ones = rank % 10;
            int tens = rank % 100;
            string suffix = (ones == 1 && tens != 11) ? "st"
                          : (ones == 2 && tens != 12) ? "nd"
                          : (ones == 3 && tens != 13) ? "rd"
                          : "th";
            return $"{rank}{suffix}";
        }
    }
}

