using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.HiddenAgenda.Services.State.Games.Data;
using KnockBox.HiddenAgenda.Services.Logic.Games;
using KnockBox.HiddenAgenda.Services.Logic.Games.Data;
using KnockBox.Core.Services.State.Users;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Primitives.Returns;
using Microsoft.AspNetCore.Components;

namespace KnockBox.HiddenAgenda.Pages
{
    public partial class TurnPhase : ComponentBase
    {
        [Parameter, EditorRequired] public HiddenAgendaGameState GameState { get; set; } = default!;
        [Parameter, EditorRequired] public HiddenAgendaGameEngine Engine { get; set; } = default!;
        [Parameter, EditorRequired] public IUserService UserService { get; set; } = default!;

        private string? _selectedTargetPlayerId;
        private string? _errorMessage;
        private int _errorKey;

        private bool IsCurrentPlayer => UserService.CurrentUser?.Id == GameState.TurnManager.CurrentPlayer;
        private HiddenAgendaPlayerState? CurrentPlayerState => GameState.TurnManager.CurrentPlayer != null && GameState.GamePlayers.TryGetValue(GameState.TurnManager.CurrentPlayer, out var p) ? p : null;
        private string CurrentPlayerName => CurrentPlayerState?.DisplayName ?? "Unknown";

        private List<SecretTask>? PlayerTasks => UserService.CurrentUser != null && GameState.GamePlayers.TryGetValue(UserService.CurrentUser.Id, out var p) ? p.SecretTasks : null;

        private void ShowError(string message)
        {
            _errorMessage = message;
            _errorKey++;
            StateHasChanged();
        }

        private void HandleSpin()
        {
            if (UserService.CurrentUser == null) return;
            var result = Engine.Spin(UserService.CurrentUser, GameState);
            if (result.IsFailure && result.TryGetFailure(out var err))
                ShowError(err.PublicMessage);
        }

        private void HandleSpaceClicked(int spaceId)
        {
            if (GameState.Phase != GamePhase.MovePhase || !IsCurrentPlayer || UserService.CurrentUser == null) return;

            var result = Engine.SelectDestination(UserService.CurrentUser, GameState, spaceId);
            if (result.IsFailure && result.TryGetFailure(out var err))
                ShowError(err.PublicMessage);
        }

        private void PlayEventCard()
        {
            if (UserService.CurrentUser == null || CurrentPlayerState?.HeldEventCard == null) return;

            Result result;
            var cardType = CurrentPlayerState.HeldEventCard.Type;

            if (cardType == EventCardType.Catalog)
            {
                if (string.IsNullOrEmpty(_selectedTargetPlayerId))
                {
                    ShowError("Please select a target player.");
                    return;
                }
                result = Engine.PlayCatalog(UserService.CurrentUser, GameState, _selectedTargetPlayerId);
            }
            else if (cardType == EventCardType.Detour)
            {
                if (string.IsNullOrEmpty(_selectedTargetPlayerId))
                {
                    ShowError("Please select a target player.");
                    return;
                }
                result = Engine.PlayDetour(UserService.CurrentUser, GameState, _selectedTargetPlayerId);
            }
            else
            {
                // For other event cards if any, or a generic Play command if added
                ShowError("Card play logic not implemented for this type.");
                return;
            }

            if (result.IsFailure && result.TryGetFailure(out var err))
                ShowError(err.PublicMessage);
        }

        private void SkipEventCard()
        {
            if (UserService.CurrentUser == null) return;
            var result = Engine.SkipEventCard(UserService.CurrentUser, GameState);
            if (result.IsFailure && result.TryGetFailure(out var err))
                ShowError(err.PublicMessage);
        }

        private void HandleCallVote()
        {
            if (UserService.CurrentUser == null) return;
            var result = Engine.CallVote(UserService.CurrentUser, GameState);
            if (result.IsFailure && result.TryGetFailure(out var err))
                ShowError(err.PublicMessage);
        }

        private void HandleCardSelected(int cardIndex)
        {
            if (UserService.CurrentUser == null) return;
            var result = Engine.SelectCurationCard(UserService.CurrentUser, GameState, cardIndex);
            if (result.IsFailure && result.TryGetFailure(out var err))
                ShowError(err.PublicMessage);
        }

        private void HandleEventChoice(bool swap)
        {
            if (UserService.CurrentUser == null) return;
            var result = Engine.SelectEventCardAction(UserService.CurrentUser, GameState, swap);
            if (result.IsFailure && result.TryGetFailure(out var err))
                ShowError(err.PublicMessage);
        }
    }
}
