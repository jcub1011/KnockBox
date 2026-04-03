using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM.Commands;
using KnockBox.Services.Logic.Games.Operator;
using KnockBox.Operator.Services.State;
using KnockBox.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Components.Pages.Games.Operator
{
    public partial class PlayPhase : ComponentBase
    {
        [Inject] protected OperatorGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<PlayPhase> Logger { get; set; } = default!;

        [Parameter] public OperatorGameState GameState { get; set; } = default!;

        [Parameter] public EventCallback<string> OnError { get; set; }

        protected HashSet<Guid> SelectedCardIds { get; } = new();
        protected string? TargetPlayerId { get; set; }

        protected OperatorPlayerState? CurrentPlayerState => 
            UserService.CurrentUser != null ? GameState.Context?.GamePlayers.GetValueOrDefault(UserService.CurrentUser.Id) : null;

        protected bool IsMyTurn => GameState.TurnManager.CurrentPlayer == UserService.CurrentUser?.Id;

        protected void ToggleCard(Guid cardId)
        {
            if (!IsMyTurn) return;

            if (SelectedCardIds.Contains(cardId))
                SelectedCardIds.Remove(cardId);
            else
                SelectedCardIds.Add(cardId);
            
            StateHasChanged();
        }

        protected void SelectTarget(string playerId)
        {
            if (!IsMyTurn) return;
            
            if (TargetPlayerId == playerId)
                TargetPlayerId = null;
            else
                TargetPlayerId = playerId;

            StateHasChanged();
        }

        protected async Task PlayCards()
        {
            if (!IsMyTurn || UserService.CurrentUser == null) return;

            if (SelectedCardIds.Count == 0)
            {
                await OnError.InvokeAsync("You must select at least one card to play.");
                return;
            }

            var command = new PlayCardsCommand(UserService.CurrentUser.Id, SelectedCardIds.ToList(), TargetPlayerId);
            var result = await GameEngine.ExecuteCommandAsync(GameState, command);

            if (result.TryGetFailure(out var error))
            {
                await OnError.InvokeAsync(error.PublicMessage);
                Logger.LogError("Failed to play cards: {Error}", error);
            }
            else
            {
                SelectedCardIds.Clear();
                TargetPlayerId = null;
            }
        }

        protected async Task SkipTurn()
        {
            if (!IsMyTurn || UserService.CurrentUser == null) return;

            var command = new SkipTurnCommand(UserService.CurrentUser.Id);
            var result = await GameEngine.ExecuteCommandAsync(GameState, command);

            if (result.TryGetFailure(out var error))
            {
                await OnError.InvokeAsync(error.PublicMessage);
                Logger.LogError("Failed to skip turn: {Error}", error);
            }
        }

        protected bool CanSkip()
        {
            if (!IsMyTurn || CurrentPlayerState == null) return false;
            // According to GDD, can skip if hand consists ONLY of Shield cards.
            return CurrentPlayerState.Hand.All(c => c.Type == CardType.Action && c.ActionValue == CardAction.Shield);
        }
    }
}
