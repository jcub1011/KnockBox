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
        private PeriodicTimer? _timer;

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

            _prevShoeIndex = GameState.ShoeIndex;

            GameState.OnStateDisposed += HandleGameStateDisposed;

            _stateSubscription = GameState.StateChangedEventManager.Subscribe(async () =>
            {
                bool isNewShoe = false;

                if (GameState != null && GameState.ShoeIndex > _prevShoeIndex)
                {
                    isNewShoe = true;
                    _prevShoeIndex = GameState.ShoeIndex;
                    _isAnimatingShoe = true;
                }

                // Reset the operator overlay dismissed flag when a new operator result arrives.
                if (GameState?.LastOperatorResult != _cachedOperatorResult)
                {
                    _cachedOperatorResult = GameState?.LastOperatorResult;
                    _operatorResultDismissed = false;
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

            _timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            _ = StartTimerAsync();

            await base.OnInitializedAsync();
        }

        private async Task StartTimerAsync()
        {
            try
            {
                while (await _timer!.WaitForNextTickAsync(ComponentDetached))
                {
                    try
                    {
                        if (GameState?.Context != null && GameState.GamePhase == GamePhase.Playing && IsHost())
                            GameEngine.Tick(GameState.Context, DateTimeOffset.UtcNow);

                        await InvokeAsync(StateHasChanged);
                    }
                    catch (ObjectDisposedException) { break; }
                }
            }
            catch (OperationCanceledException) { }
        }

        public override void Dispose()
        {
            _timer?.Dispose();
            if (GameState != null)
                GameState.OnStateDisposed -= HandleGameStateDisposed;
            _stateSubscription?.Dispose();
            GameSessionService.LeaveCurrentSession(false);
            base.Dispose();
        }

        private void HandleGameStateDisposed()
        {
            try
            {
                // The host left and the game state was torn down. Navigate remaining players home.
                _ = InvokeAsync(ReturnToHome).ContinueWith(
                    t => Logger.LogError(t.Exception, "Error navigating home after game state was disposed."),
                    System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error handling game state disposal in lobby.");
            }
        }

        protected CardCounterGameState? GameState { get; set; }

        protected TimeSpan GameTime => GameState != null ? DateTime.UtcNow - GameState.CreatedAt : TimeSpan.Zero;

        // ── Discard history overlay ───────────────────────────────────────────
        private bool _showDiscardOverlay = false;

        // ── Shoe animation ────────────────────────────────────────────────────
        private const int ShoeAnimationDurationMs = 2500;
        private int _prevShoeIndex = -1;
        private bool _isAnimatingShoe = false;

        // ── Target selection state ────────────────────────────────────────────
        private int? _pendingActionCardIndex;
        private string? _selectedTargetId;

        // ── Skim digit selection state ────────────────────────────────────────
        private int? _pendingSkimSourceDigit;
        private int? _pendingSkimTargetDigit;

        // ── Not My Money target selection state ───────────────────────────────
        private string? _notMyMoneyTargetId;

        // ── Operator result overlay state ─────────────────────────────────────
        private OperatorResultInfo? _cachedOperatorResult;
        private bool _operatorResultDismissed;

        // ── Reorder state ─────────────────────────────────────────────────────
        protected List<int> SelectedReorderIndices = new();

        // ── Discard state ─────────────────────────────────────────────────────
        private HashSet<int> _selectedDiscardIndices = new();

        // ── Helpers ───────────────────────────────────────────────────────────

        protected void ToggleDiscardOverlay() => _showDiscardOverlay = !_showDiscardOverlay;

        protected void ReturnToHome() => NavigationService.ToHome();

        protected bool IsHost()
        {
            if (GameState == null || UserService.CurrentUser == null) return false;
            return GameState.Host.Id == UserService.CurrentUser.Id;
        }

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

        /// <summary>Returns whether the Skim card can be played by the current player.</summary>
        protected bool CanPlaySkim()
        {
            var me = GetMyPlayer();
            return me != null && me.Pot.Count > 0;
        }

        /// <summary>Returns whether a given player can be targeted by Skim (non-empty pot, not self).</summary>
        protected bool CanSkimTarget(PlayerState target)
        {
            var me = GetMyPlayer();
            return me != null && target.Pot.Count > 0 && target.PlayerId != me.PlayerId;
        }

        private void ClearTransientUiState()
        {
            _pendingActionCardIndex = null;
            _selectedTargetId = null;
            _selectedDiscardIndices.Clear();
            _pendingSkimSourceDigit = null;
            _pendingSkimTargetDigit = null;
            _notMyMoneyTargetId = null;
        }

        // ── Actions ───────────────────────────────────────────────────────────

        protected async Task StartGame()
        {
            if (GameState == null || UserService.CurrentUser == null) return;
            var result = await GameEngine.StartAsync(UserService.CurrentUser, GameState);
            if (result.TryGetFailure(out var error))
                Logger.LogError("Failed to start game: {Error}", error);
        }

        protected void NotifyConfigChanged()
        {
            GameState?.StateChangedEventManager.Notify();
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

            // These cards cannot be played as standard targeted/untargeted actions during normal turn
            // Comp'd is only for reactions, and Not My Money is only prompted on operator draw.
            if (card.Action == ActionType.NotMyMoney || card.Action == ActionType.Compd)
            {
                // However, Comp'd can be played if responding to a reaction/feeling lucky!
                bool isReaction = GameState.PendingReaction?.TargetId == UserService.CurrentUser.Id || GameState.FeelingLuckyTargetId == UserService.CurrentUser.Id;
                if (!isReaction) return;

                // Not My Money cannot be clicked from hand at all now.
                if (card.Action == ActionType.NotMyMoney) return;
            }

            if (RequiresTarget(card.Action))
            {
                _pendingActionCardIndex = cardIndex;
                _selectedTargetId = null;
                _pendingSkimSourceDigit = null;
                _pendingSkimTargetDigit = null;
            }
            else
            {
                GameEngine.PlayActionCard(UserService.CurrentUser, GameState, cardIndex);
            }
        }

        protected void SelectTarget(string playerId)
        {
            if (IsHost()) return; // host cannot target
            _selectedTargetId = playerId;
        }

        protected void ConfirmPlayWithTarget()
        {
            if (GameState == null || UserService.CurrentUser == null) return;
            if (_pendingActionCardIndex == null || _selectedTargetId == null) return;
            var me = GetMyPlayer();
            if (me == null) return;
            var card = me.ActionHand.ElementAtOrDefault(_pendingActionCardIndex.Value);
            if (card == null) return;

            // For Skim, validation happens in the engine; we just send the card play command
            GameEngine.PlayActionCard(UserService.CurrentUser, GameState, _pendingActionCardIndex.Value, _selectedTargetId);
            _pendingActionCardIndex = null;
            _selectedTargetId = null;
            _pendingSkimSourceDigit = null;
            _pendingSkimTargetDigit = null;
        }

        protected void CancelTargetSelect()
        {
            _pendingActionCardIndex = null;
            _selectedTargetId = null;
            _pendingSkimSourceDigit = null;
            _pendingSkimTargetDigit = null;
        }

        // ── Skim digit selection ──────────────────────────────────────────────

        protected bool IsInSkimDigitSelect()
        {
            if (GameState == null || UserService.CurrentUser == null) return false;
            return GameState.PendingReaction?.PlayedCard.Action == ActionType.Skim
                   && GameState.PendingReaction.SourceId == UserService.CurrentUser.Id;
        }

        protected void SelectSkimSourceDigit(int index)
        {
            _pendingSkimSourceDigit = index;
        }

        protected void SelectSkimTargetDigit(int index)
        {
            _pendingSkimTargetDigit = index;
        }

        protected void ConfirmSkimSwap()
        {
            if (GameState == null || UserService.CurrentUser == null) return;
            if (_pendingSkimSourceDigit == null || _pendingSkimTargetDigit == null) return;
            GameEngine.SkimSelect(UserService.CurrentUser, GameState, _pendingSkimSourceDigit.Value, _pendingSkimTargetDigit.Value);
        }

        protected void CancelSkimSelect()
        {
            _pendingSkimSourceDigit = null;
            _pendingSkimTargetDigit = null;
        }

        // ── Not My Money target selection ─────────────────────────────────────

        protected bool IsInNotMyMoneySelect()
        {
            if (GameState == null || UserService.CurrentUser == null) return false;
            // Show Not My Money target selection to the active player when the state is waiting for their choice
            if (!GameState.IsNotMyMoneySelecting) return false;
            var activeId = GameState.TurnOrder.Count > 0 ? GameState.TurnOrder[GameState.CurrentPlayerIndex] : "";
            return activeId == UserService.CurrentUser.Id;
        }

        protected void SelectNotMyMoneyTarget(string playerId)
        {
            _notMyMoneyTargetId = playerId;
        }

        protected void ConfirmNotMyMoneyTarget()
        {
            if (GameState == null || UserService.CurrentUser == null) return;
            if (_notMyMoneyTargetId == null) return;
            GameEngine.NotMyMoneySelectTarget(UserService.CurrentUser, GameState, _notMyMoneyTargetId);
            _notMyMoneyTargetId = null;
        }

        protected void CancelNotMyMoney()
        {
            if (GameState == null || UserService.CurrentUser == null) return;
            GameEngine.NotMyMoneyCancel(UserService.CurrentUser, GameState);
            _notMyMoneyTargetId = null;
        }

        protected void DismissOperatorResult()
        {
            _operatorResultDismissed = true;
        }

        // ── Discard ───────────────────────────────────────────────────────────

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

        // ── Reorder ───────────────────────────────────────────────────────────

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

        // ── Game reset ────────────────────────────────────────────────────────

        protected void ResetGame()
        {
            if (GameState == null || UserService.CurrentUser == null) return;
            var result = GameEngine.ResetGame(UserService.CurrentUser, GameState);
            if (result.TryGetFailure(out var error))
                Logger.LogError("Failed to reset game: {Error}", error);
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
                "Swap any digit in your pot with any digit in a chosen opponent's pot. Cannot be played or target players with empty pots. Blockable with Comp'd.",
            ActionType.Burn =>
                "Discard the top card of the shoe without drawing it. Useful for removing dangerous cards.",
            ActionType.TurnTheTable =>
                "Reverse the digit order of a chosen opponent's pot. Target required. Blockable with Comp'd.",
            ActionType.Compd =>
                "Block a card played against you (Feeling Lucky, Skim, Turn The Table, or Launder). Hold until needed.",
            ActionType.NotMyMoney =>
                "When you draw an operator card, redirect it to apply to another player's pot instead.",
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

        protected static string FormatBalance(double balance)
        {
            return balance >= 0 ? $"+{balance:N0}" : $"{balance:N0}";
        }

        protected static string FormatBaseCardDisplay(BaseCard card) => card switch
        {
            NumberCard nc => $"{nc.Value}",
            OperatorCard oc => oc.Op switch
            {
                Operator.Add => "+",
                Operator.Subtract => "−",
                Operator.Multiply => "×",
                Operator.Divide => "÷",
                _ => "?"
            },
            _ => "?"
        };

        protected static string GetBaseCardTypeLabel(BaseCard card) => card switch
        {
            NumberCard => "NUMBER",
            OperatorCard => "OPERATOR",
            _ => "CARD"
        };
    }
}
