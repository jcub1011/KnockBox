using KnockBox.Services.Logic.Games.CardCounter;
using KnockBox.Services.State.Games.CardCounter;
using KnockBox.Services.State.Games.CardCounter.Data;
using KnockBox.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Components.Pages.Games.CardCounter
{
    public partial class BuyInPhase : ComponentBase
    {
        [Inject] protected CardCounterGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Parameter] public CardCounterGameState GameState { get; set; } = default!;

        protected PlayerState? GetMyPlayer()
        {
            if (UserService.CurrentUser == null) return null;
            return GameState.GamePlayers.TryGetValue(UserService.CurrentUser.Id, out var state) ? state : null;
        }

        protected void SetBuyIn(bool isNegative)
        {
            if (UserService.CurrentUser == null) return;
            GameEngine.SetBuyIn(UserService.CurrentUser, GameState, isNegative);
        }
    }
}
