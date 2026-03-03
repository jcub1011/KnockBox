using KnockBox.Components.Shared;
using KnockBox.Services.Navigation;
using KnockBox.Services.Navigation.Games.CardCounter;
using KnockBox.Services.State.Games.CardCounter;
using KnockBox.Services.State.Games.CardCounter.Data;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Components.Pages.Games.CardCounter
{
    public partial class CardCounterLobby : DisposableComponent
    {
        [Inject] protected CardCounterGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IGameSessionService GameSessionService { get; set; } = default!;

        [Inject] protected INavigationService NavigationService { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<CardCounterLobby> Logger { get; set; } = default!;

        [Parameter] public string ObfuscatedRoomCode { get; set; } = default!;

        private IDisposable? _stateSubscription;

        protected override async Task OnInitializedAsync()
        {
            if (!GameSessionService.TryGetCurrentSession(out var session))
            {
                ReturnToHome();
                return;
            }

            if (session.LobbyRegistration.State is not CardCounterGameState gameState)
            {
                Logger.LogError("Game state is not of type {Type}", nameof(CardCounterGameState));
                ReturnToHome();
                return;
            }

            GameState = gameState;
            RoomCode = session.LobbyRegistration.Code;

            _stateSubscription = GameState.SubscribeToStateChanged(async () => await InvokeAsync(StateHasChanged)).Value;

            await base.OnInitializedAsync();
        }

        public override void Dispose()
        {
            _stateSubscription?.Dispose();
            GameSessionService.LeaveCurrentSession(false);
            base.Dispose();
        }

        protected CardCounterGameState? GameState { get; set; }
        protected string RoomCode { get; set; } = string.Empty;
        protected bool IsRoomCodeVisible { get; set; } = false;

        protected void ToggleRoomCode() => IsRoomCodeVisible = !IsRoomCodeVisible;

        protected void ReturnToHome()
        {
            NavigationService.ToHome();
        }

        protected async Task StartGame()
        {
            if (GameState == null || UserService.CurrentUser == null) return;
            var result = await GameEngine.StartAsync(UserService.CurrentUser, GameState);
            if (result.IsFailure)
            {
                Logger.LogError("Failed to start game: {Error}", result.Error?.Message);
            }
        }

        protected PlayerState? GetMyPlayer()
        {
            if (GameState == null || UserService.CurrentUser == null) return null;
            return GameState.GamePlayers.TryGetValue(UserService.CurrentUser.Id, out var state) ? state : null;
        }

        protected void SetBuyIn(bool isNegative)
        {
            if (GameState == null || UserService.CurrentUser == null) return;
            GameEngine.SetBuyIn(UserService.CurrentUser, GameState, isNegative);
        }

        protected void DrawCard()
        {
            if (GameState == null || UserService.CurrentUser == null) return;
            GameEngine.DrawCard(UserService.CurrentUser, GameState);
        }

        protected void PlayActionCard(int cardIndex)
        {
            if (GameState == null || UserService.CurrentUser == null) return;
            GameEngine.PlayActionCard(UserService.CurrentUser, GameState, cardIndex);
        }

        protected void PassTurn()
        {
            if (GameState == null || UserService.CurrentUser == null) return;
            GameEngine.PassTurn(UserService.CurrentUser, GameState);
        }

        protected List<int> SelectedReorderIndices = new();

        protected void ToggleReorderIndex(int index)
        {
            if (!SelectedReorderIndices.Contains(index))
            {
                SelectedReorderIndices.Add(index);
            }
        }

        protected void ResetReorder()
        {
            SelectedReorderIndices.Clear();
        }

        protected void SubmitReorder()
        {
            if (GameState == null || UserService.CurrentUser == null) return;
            var player = GetMyPlayer();
            if (player == null || player.PrivateReveal == null) return;

            if (SelectedReorderIndices.Count == player.PrivateReveal.Count)
            {
                GameEngine.SubmitReorder(UserService.CurrentUser, GameState, SelectedReorderIndices.ToArray());
                SelectedReorderIndices.Clear();
            }
        }

        protected void FoldPot()
        {
            if (GameState == null || UserService.CurrentUser == null) return;
            GameEngine.FoldPot(UserService.CurrentUser, GameState);
        }
    }
}