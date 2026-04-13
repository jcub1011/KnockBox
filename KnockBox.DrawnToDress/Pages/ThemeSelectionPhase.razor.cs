using KnockBox.Services.Logic.Games.DrawnToDress;
using KnockBox.Services.Logic.Games.DrawnToDress.FSM;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;
using KnockBox.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DrawnToDress.Pages
{
    public partial class ThemeSelectionPhase : ComponentBase
    {
        [Inject] protected DrawnToDressGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<ThemeSelectionPhase> Logger { get; set; } = default!;

        [Parameter] public DrawnToDressGameState GameState { get; set; } = default!;

        private string? _themeText;
        private string? _errorMessage;

        private string CurrentPlayerId => UserService.CurrentUser?.Id ?? string.Empty;

        protected void SubmitHostTheme()
        {
            if (string.IsNullOrWhiteSpace(_themeText)) return;
            SendCommand(new SelectThemeCommand(CurrentPlayerId, _themeText.Trim()), "select theme");
        }

        protected void SubmitPlayerTheme()
        {
            if (string.IsNullOrWhiteSpace(_themeText)) return;
            SendCommand(new SubmitPlayerThemeCommand(CurrentPlayerId, _themeText.Trim()), "submit theme");
        }

        protected void VoteForTheme(string themeId)
        {
            SendCommand(new VoteForThemeCommand(CurrentPlayerId, themeId), "vote for theme");
        }

        private void SendCommand(DrawnToDressCommand cmd, string action)
        {
            if (GameState.Context is null) return;

            _errorMessage = null;

            try
            {
                var result = GameEngine.ProcessCommand(GameState.Context, cmd);
                if (result.TryGetFailure(out var err))
                {
                    _errorMessage = err.PublicMessage;
                    Logger.LogWarning("Theme selection — {action} failed: {msg}", action, err.PublicMessage);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected error during {action}.", action);
                _errorMessage = "An unexpected error occurred. Please try again.";
            }
            finally
            {
                StateHasChanged();
            }
        }
    }
}

