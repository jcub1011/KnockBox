using KnockBox.Components.Shared;
using KnockBox.Services.Logic.Games.CardCounter;
using KnockBox.Services.Navigation;
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

            _stateSubscription = GameState.StateChangedEventManager.Subscribe(async () =>
            {
                bool isNewShoe = GameState?.IsNewShoe == true;

                if (isNewShoe && GameState != null)
                {
                    GameState.IsNewShoe = false;
                    _prevShoeIndex = GameState.ShoeIndex;
                    _isAnimatingShoe = true;
                }

                ClearTransientUiState();
                await InvokeAsync(StateHasChanged);

                if (isNewShoe)
                {
                    await Task.Delay(ShoeAnimationDurationMs);
                    _isAnimatingShoe = false;
                    await InvokeAsync(StateHasChanged);
                }
            });

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

        // ── Discard history overlay ───────────────────────────────────────────
        private bool _showDiscardOverlay = false;

        // ── Shoe animation ────────────────────────────────────────────────────
        private const int ShoeAnimationDurationMs = 2500;
        private int _prevShoeIndex = -1;
        private bool _isAnimatingShoe = false;

        // ── Target selection state ────────────────────────────────────────────
        private int? _pendingActionCardIndex;
        private string? _selectedTargetId;

        // ── Reorder state ─────────────────────────────────────────────────────
        protected List<int> SelectedReorderIndices = new();

        // ── Discard state ─────────────────────────────────────────────────────
        private HashSet<int> _selectedDiscardIndices = new();

        // ── Helpers ───────────────────────────────────────────────────────────

        protected void ToggleRoomCode() => IsRoomCodeVisible = !IsRoomCodeVisible;

        protected void ToggleDiscardOverlay() => _showDiscardOverlay = !_showDiscardOverlay;

        protected void ReturnToHome() => NavigationService.ToHome();

        protected PlayerState? GetMyPlayer()
        {
            if (GameState == null || UserService.CurrentUser == null) return null;
            return GameState.GamePlayers.TryGetValue(UserService.CurrentUser.Id, out var state) ? state : null;
        }

        protected bool IsActivePlayer()
        {
            if (GameState == null || UserService.CurrentUser == null) return false;
            return GameState.TurnOrder.Count > 0 &&
                   GameState.TurnOrder[GameState.CurrentPlayerIndex] == UserService.CurrentUser.Id;
        }

        protected bool IsOverHandLimit()
        {
            var me = GetMyPlayer();
            return me != null && GameState != null && me.ActionHand.Count > GameState.Config.ActionHandLimit;
        }

        protected int DiscardNeeded()
        {
            var me = GetMyPlayer();
            if (me == null || GameState == null) return 0;
            return Math.Max(0, me.ActionHand.Count - GameState.Config.ActionHandLimit);
        }

        protected bool CanConfirmDiscard()
        {
            var me = GetMyPlayer();
            if (me == null || GameState == null) return false;
            int afterDiscard = me.ActionHand.Count - _selectedDiscardIndices.Count;
            return afterDiscard <= GameState.Config.ActionHandLimit && _selectedDiscardIndices.Count > 0;
        }

        private void ClearTransientUiState()
        {
            _pendingActionCardIndex = null;
            _selectedTargetId = null;
            _selectedDiscardIndices.Clear();
        }

        // ── Actions ───────────────────────────────────────────────────────────

        protected async Task StartGame()
        {
            if (GameState == null || UserService.CurrentUser == null) return;
            var result = await GameEngine.StartAsync(UserService.CurrentUser, GameState);
            if (result.TryGetFailure(out var error))
                Logger.LogError("Failed to start game: {Error}", error);
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

        protected void PassTurn()
        {
            if (GameState == null || UserService.CurrentUser == null) return;
            GameEngine.PassTurn(UserService.CurrentUser, GameState);
        }

        protected void FoldPot()
        {
            if (GameState == null || UserService.CurrentUser == null) return;
            GameEngine.FoldPot(UserService.CurrentUser, GameState);
        }

        protected void AcceptPending()
        {
            if (GameState == null || UserService.CurrentUser == null) return;
            GameEngine.AcceptPending(UserService.CurrentUser, GameState);
        }

        protected void OnActionCardClick(int cardIndex)
        {
            if (GameState == null || UserService.CurrentUser == null) return;
            var me = GetMyPlayer();
            if (me == null || cardIndex < 0 || cardIndex >= me.ActionHand.Count) return;

            var card = me.ActionHand[cardIndex];
            if (RequiresTarget(card.Action))
            {
                _pendingActionCardIndex = cardIndex;
                _selectedTargetId = null;
            }
            else
            {
                GameEngine.PlayActionCard(UserService.CurrentUser, GameState, cardIndex);
            }
        }

        protected void SelectTarget(string playerId)
        {
            _selectedTargetId = playerId;
        }

        protected void ConfirmPlayWithTarget()
        {
            if (GameState == null || UserService.CurrentUser == null) return;
            if (_pendingActionCardIndex == null || _selectedTargetId == null) return;
            GameEngine.PlayActionCard(UserService.CurrentUser, GameState, _pendingActionCardIndex.Value, _selectedTargetId);
            _pendingActionCardIndex = null;
            _selectedTargetId = null;
        }

        protected void CancelTargetSelect()
        {
            _pendingActionCardIndex = null;
            _selectedTargetId = null;
        }

        protected void ToggleDiscardSelection(int index)
        {
            if (!_selectedDiscardIndices.Remove(index))
                _selectedDiscardIndices.Add(index);
        }

        protected void ConfirmDiscard()
        {
            if (GameState == null || UserService.CurrentUser == null) return;
            if (!CanConfirmDiscard()) return;
            GameEngine.DiscardActionCards(UserService.CurrentUser, GameState, _selectedDiscardIndices.ToArray());
            _selectedDiscardIndices.Clear();
        }

        protected void SelectForReorder(int index)
        {
            if (!SelectedReorderIndices.Contains(index))
                SelectedReorderIndices.Add(index);
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

        // ── Static helpers ────────────────────────────────────────────────────

        protected static bool RequiresTarget(ActionType action) => action switch
        {
            ActionType.Skim => true,
            ActionType.TurnTheTable => true,
            ActionType.Launder => true,
            _ => false
        };

        protected static string GetActionCardName(ActionType action) => action switch
        {
            ActionType.FeelingLucky => "Feeling Lucky",
            ActionType.MakeMyLuck => "Make My Luck",
            ActionType.Skim => "Skim",
            ActionType.Burn => "Burn",
            ActionType.TurnTheTable => "Turn The Table",
            ActionType.Compd => "Comp'd",
            ActionType.NotMyMoney => "Not My Money",
            ActionType.Launder => "Launder",
            _ => action.ToString()
        };

        protected static string GetActionCardIcon(ActionType action) => action switch
        {
            ActionType.FeelingLucky => "🎲",
            ActionType.MakeMyLuck => "⭐",
            ActionType.Skim => "✂️",
            ActionType.Burn => "🔥",
            ActionType.TurnTheTable => "🔄",
            ActionType.Compd => "🛡️",
            ActionType.NotMyMoney => "💸",
            ActionType.Launder => "🧺",
            _ => "🃏"
        };

        protected static string GetActionCardDescription(ActionType action) => action switch
        {
            ActionType.FeelingLucky =>
                "Force the next player to draw a card from the shoe. They may chain it with their own Feeling Lucky or block with Comp'd.",
            ActionType.MakeMyLuck =>
                "Peek at the top 3 cards in the shoe and rearrange them in any order you choose.",
            ActionType.Skim =>
                "Swap the last digit in your pot with the last digit in a chosen opponent's pot. Target required. Blockable with Comp'd.",
            ActionType.Burn =>
                "Discard the top card of the shoe without drawing it. Useful for removing dangerous cards.",
            ActionType.TurnTheTable =>
                "Reverse the digit order of a chosen opponent's pot. Target required. Blockable with Comp'd.",
            ActionType.Compd =>
                "Block a card played against you (Feeling Lucky, Skim, Turn The Table, or Launder). Hold until needed.",
            ActionType.NotMyMoney =>
                "Redirect the next card you draw to another player instead of keeping it yourself.",
            ActionType.Launder =>
                "Swap your entire pot with a chosen opponent's pot. Target required. Blockable with Comp'd.",
            _ => action.ToString()
        };

        protected static string GetActionCardColor(ActionType action) => action switch
        {
            ActionType.FeelingLucky => "cc-card-lucky",
            ActionType.MakeMyLuck => "cc-card-luck",
            ActionType.Skim => "cc-card-skim",
            ActionType.Burn => "cc-card-burn",
            ActionType.TurnTheTable => "cc-card-turn",
            ActionType.Compd => "cc-card-compd",
            ActionType.NotMyMoney => "cc-card-money",
            ActionType.Launder => "cc-card-launder",
            _ => ""
        };

        protected static string GetOperatorSymbol(Operator op) => op switch
        {
            Operator.Add => "+",
            Operator.Subtract => "−",
            Operator.Multiply => "×",
            Operator.Divide => "÷",
            _ => "?"
        };

        protected static string FormatBalance(long balance)
        {
            return balance >= 0 ? $"+{balance:N0}" : $"{balance:N0}";
        }
    }
}
