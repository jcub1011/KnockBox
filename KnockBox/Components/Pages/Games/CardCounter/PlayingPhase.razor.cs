using KnockBox.Services.Logic.Games.CardCounter;
using KnockBox.Services.State.Games.CardCounter;
using KnockBox.Services.State.Games.CardCounter.Data;
using KnockBox.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Components.Pages.Games.CardCounter
{
    public partial class PlayingPhase : ComponentBase
    {
        [Inject] protected CardCounterGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Parameter] public CardCounterGameState GameState { get; set; } = default!;

        [Parameter] public bool IsAnimatingShoe { get; set; }

        // ── Discard history overlay ───────────────────────────────────────────
        private bool _showDiscardOverlay = false;

        // ── Game state change tracking (for clearing transient UI state) ──────
        private GamePhase? _lastKnownPhase;
        private int _lastKnownPlayerIndex = -1;

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
        private int _toastKey;

        // ── Operator change toast state (Active Operator Mode) ────────────────
        private OperatorChangeInfo? _cachedOperatorChange;
        private bool _operatorChangeDismissed;
        private int _opChangeToastKey;

        // ── Reorder state ─────────────────────────────────────────────────────
        protected List<int> SelectedReorderIndices = new();

        // ── Discard state ─────────────────────────────────────────────────────
        private HashSet<int> _selectedDiscardIndices = new();

        protected TimeSpan GameTime => GameState != null ? DateTime.UtcNow - GameState.CreatedAt : TimeSpan.Zero;

        protected override void OnParametersSet()
        {
            // Reset the operator overlay dismissed flag when a new operator result arrives.
            // Use reference equality so that two consecutive draws with identical values
            // (same before/after balance) each trigger the toast independently.
            if (!ReferenceEquals(GameState?.LastOperatorResult, _cachedOperatorResult))
            {
                _cachedOperatorResult = GameState?.LastOperatorResult;
                _operatorResultDismissed = false;
                _toastKey++;
                // Dismiss the operator-change toast when a new balance result arrives so the
                // two toasts never overlap.
                _operatorChangeDismissed = true;
            }

            if (!ReferenceEquals(GameState?.LastOperatorChange, _cachedOperatorChange))
            {
                _cachedOperatorChange = GameState?.LastOperatorChange;
                _operatorChangeDismissed = false;
                _opChangeToastKey++;
                // Dismiss the balance-change toast when the operator changes so the
                // two toasts never overlap.
                _operatorResultDismissed = true;
            }

            // Only clear transient UI state (pending card, selected target, etc.) when the
            // game phase or active player actually changes — not on every tick notification.
            var currentPhase = GameState?.GamePhase;
            var currentPlayerIndex = GameState?.CurrentPlayerIndex ?? -1;
            if (currentPhase != _lastKnownPhase || currentPlayerIndex != _lastKnownPlayerIndex)
            {
                _lastKnownPhase = currentPhase;
                _lastKnownPlayerIndex = currentPlayerIndex;
                ClearTransientUiState();
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        protected void ToggleDiscardOverlay() => _showDiscardOverlay = !_showDiscardOverlay;

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

        /// <summary>Returns whether the Hedge Your Bet card can be played (shoe must have cards).</summary>
        protected bool CanPlayHedgeYourBet()
        {
            return GameState != null && GameState.CurrentShoe.Count > 0;
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

        protected void DrawCard()
        {
            if (GameState == null || UserService.CurrentUser == null) return;
            GameEngine.DrawCard(UserService.CurrentUser, GameState);
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

        protected void DismissOperatorChange()
        {
            _operatorChangeDismissed = true;
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
            ActionType.Tilt => "Tilt",
            ActionType.HedgeYourBet => "Hedge Your Bet",
            ActionType.LetItRide => "Let It Ride",
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
            ActionType.Tilt => "🎰",
            ActionType.HedgeYourBet => "🎯",
            ActionType.LetItRide => "🔁",
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
            ActionType.Tilt =>
                "Shuffle all number cards from every player's pot into one pool, then redistribute them evenly. Extra cards are dealt one at a time starting from you, in turn order.",
            ActionType.HedgeYourBet =>
                "Convert the next card drawn from the shoe into a + if your balance is negative, or a − if your balance is zero or positive. Does not draw immediately. Only playable when the shoe is not empty.",
            ActionType.LetItRide =>
                "Grant yourself an extra turn after your current turn ends. Can be stacked: each card played adds one additional turn.",
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
            ActionType.Tilt => "cc-card-tilt",
            ActionType.HedgeYourBet => "cc-card-hedge",
            ActionType.LetItRide => "cc-card-letitride",
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
