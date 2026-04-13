using KnockBox.ConsultTheCard.Services.Logic.Games;
using KnockBox.ConsultTheCard.Services.State.Games;
using KnockBox.Core.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.ConsultTheCard.Pages
{
    public partial class RevealPhase : ComponentBase
    {
        [Inject] protected ConsultTheCardGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<RevealPhase> Logger { get; set; } = default!;

        [Parameter] public ConsultTheCardGameState GameState { get; set; } = default!;

        [Parameter] public EventCallback<string> OnError { get; set; }

        private string _guessText = string.Empty;

        protected void SubmitGuess()
        {
            if (UserService.CurrentUser == null || string.IsNullOrWhiteSpace(_guessText)) return;

            var result = GameEngine.InformantGuess(UserService.CurrentUser, GameState, _guessText.Trim());
            if (result.TryGetFailure(out var error))
            {
                Logger.LogError("Failed to submit Informant guess: {Error}", error);
                _ = OnError.InvokeAsync("Action not available right now.");
            }
            else
            {
                _guessText = string.Empty;
            }
        }
    }
}

