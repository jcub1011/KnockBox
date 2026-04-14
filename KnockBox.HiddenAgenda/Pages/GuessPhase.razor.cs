using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.HiddenAgenda.Services.State.Games.Data;
using KnockBox.HiddenAgenda.Services.Logic.Games;
using KnockBox.Core.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.HiddenAgenda.Pages
{
    public partial class GuessPhase : ComponentBase
    {
        [Parameter, EditorRequired] public HiddenAgendaGameState GameState { get; set; } = default!;
        [Parameter, EditorRequired] public HiddenAgendaGameEngine Engine { get; set; } = default!;
        [Parameter, EditorRequired] public IUserService UserService { get; set; } = default!;

        private string? _errorMessage;
        private int _errorKey;

        private bool IsCurrentPlayer => UserService.CurrentUser?.Id == GameState.TurnManager.CurrentPlayer;
        private string CurrentPlayerName => GameState.TurnManager.CurrentPlayer != null && GameState.GamePlayers.TryGetValue(GameState.TurnManager.CurrentPlayer, out var p) ? p.DisplayName : "Unknown";
        private List<HiddenAgendaPlayerState> Opponents => GameState.GamePlayers.Values.Where(p => p.PlayerId != UserService.CurrentUser?.Id).ToList();

        private void ShowError(string message)
        {
            _errorMessage = message;
            _errorKey++;
            StateHasChanged();
        }

        private void HandleSubmit(Dictionary<string, List<string>> guesses)
        {
            if (UserService.CurrentUser == null) return;
            var result = Engine.SubmitGuess(UserService.CurrentUser, GameState, guesses);
            if (result.IsFailure && result.TryGetFailure(out var err))
                ShowError(err.PublicMessage);
        }

        private void HandleSkip()
        {
            if (UserService.CurrentUser == null) return;
            var result = Engine.SkipGuess(UserService.CurrentUser, GameState);
            if (result.IsFailure && result.TryGetFailure(out var err))
                ShowError(err.PublicMessage);
        }
    }
}
