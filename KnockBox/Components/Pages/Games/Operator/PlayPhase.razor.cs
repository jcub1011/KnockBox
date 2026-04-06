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

        [Parameter] public bool IsReadOnly { get; set; }

        private Card? _pendingAction;
        private Guid? _selectedOperatorId;
        private readonly List<Guid> _selectedNumberIds = new();
        private string? _targetPlayerId;
        private bool _waitingForTarget;

        protected OperatorPlayerState? CurrentPlayerState =>
            UserService.CurrentUser != null ? GameState.Context?.GamePlayers.GetValueOrDefault(UserService.CurrentUser.Id) : null;

        protected bool IsMyTurn => !IsReadOnly && GameState.TurnManager.CurrentPlayer == UserService.CurrentUser?.Id;

        protected HashSet<Guid> NewCardIds
        {
            get
            {
                var ps = CurrentPlayerState;
                if (ps?.PreDrawCardIds == null || ps.PreDrawCardIds.Count == 0) return new();
                return ps.Hand
                    .Where(c => !ps.PreDrawCardIds.Contains(c.Id))
                    .Select(c => c.Id)
                    .ToHashSet();
            }
        }

        protected HashSet<Guid> SelectedCardIds
        {
            get
            {
                var ids = new HashSet<Guid>(_selectedNumberIds);
                if (_pendingAction != null) ids.Add(_pendingAction.Id);
                if (_selectedOperatorId != null) ids.Add(_selectedOperatorId.Value);
                return ids;
            }
        }

        protected bool ShowSubmit =>
            (_selectedNumberIds.Count > 0 && (_pendingAction == null || !ActionNeedsTarget(_pendingAction) || _targetPlayerId != null))
            || (_selectedOperatorId != null && _targetPlayerId != null)
            || (_pendingAction != null && !ActionNeedsNumbers(_pendingAction)
                && (!ActionNeedsTarget(_pendingAction) || _targetPlayerId != null));

        protected bool NeedsTargetSelection =>
            _waitingForTarget
            || (_pendingAction != null && ActionNeedsTarget(_pendingAction) && _targetPlayerId == null)
            || (_selectedOperatorId != null && _targetPlayerId == null);

        protected HashSet<Guid> DisabledCardIds
        {
            get
            {
                if (CurrentPlayerState == null || !IsMyTurn || GameState.Context == null)
                    return CurrentPlayerState?.Hand.Select(c => c.Id).ToHashSet() ?? new();

                var hand = CurrentPlayerState.Hand;
                var disabled = new HashSet<Guid>();
                var context = GameState.Context;

                // Waiting for target — disable all hand cards
                if (_waitingForTarget)
                {
                    foreach (var c in hand)
                        disabled.Add(c.Id);
                    return disabled;
                }

                // Operator selected — disable everything except the selected operator
                if (_selectedOperatorId != null)
                {
                    foreach (var c in hand.Where(c => c.Id != _selectedOperatorId.Value))
                        disabled.Add(c.Id);
                    return disabled;
                }

                // Action pending that needs numbers — only numbers + the action itself enabled
                if (_pendingAction != null)
                {
                    if (_pendingAction is IPairableCard pairable)
                    {
                        var pairings = pairable.GetPotentialPairingCards(context, CurrentPlayerState).ToList();
                        foreach (var c in hand.Where(c => !pairings.Contains(c) && c.Id != _pendingAction.Id))
                            disabled.Add(c.Id);
                    }
                    else
                    {
                        foreach (var c in hand.Where(c => c.Id != _pendingAction.Id))
                            disabled.Add(c.Id);
                    }
                    return disabled;
                }

                // Numbers selected, no action — numbers + actions needing numbers enabled
                if (_selectedNumberIds.Count > 0)
                {
                    foreach (var c in hand)
                    {
                        if (c is NumberCard) continue;
                        if (c is IPairableCard pairable && pairable.GetPotentialPairingCards(context, CurrentPlayerState).Any(num => _selectedNumberIds.Contains(num.Id))) continue;
                        disabled.Add(c.Id);
                    }
                    return disabled;
                }

                // No selection — use IsPlayable
                foreach (var c in hand)
                {
                    if (!c.IsPlayable(context, CurrentPlayerState))
                        disabled.Add(c.Id);
                }

                return disabled;
            }
        }

        protected string? SelectionHint
        {
            get
            {
                if (_waitingForTarget)
                {
                    var actionName = _pendingAction?.TooltipName() ?? "action";
                    return $"Select target for {actionName}";
                }
                if (_selectedOperatorId != null && _targetPlayerId == null)
                    return "Select target";

                if (_pendingAction != null)
                {
                    bool needsNumbers = ActionNeedsNumbers(_pendingAction);
                    bool needsTarget = ActionNeedsTarget(_pendingAction);

                    bool hasNumbers = _selectedNumberIds.Count > 0;
                    bool hasTarget = _targetPlayerId != null;

                    if (needsNumbers && needsTarget && (!hasNumbers || !hasTarget))
                        return "Select target & numbers";
                    if (needsNumbers && !hasNumbers)
                        return "Select numbers";
                    if (needsTarget && !hasTarget)
                        return "Select target";
                }
                return null;
            }
        }

        protected string? ConcatenatedValue
        {
            get
            {
                if (_selectedNumberIds.Count < 2 || CurrentPlayerState == null) return null;
                decimal val = 0;
                foreach (var id in _selectedNumberIds)
                {
                    var card = CurrentPlayerState.Hand.FirstOrDefault(c => c.Id == id);
                    if (card == null) continue;
                    if (card is NumberCard numCard)
                    {
                        val = val * 10 + numCard.NumberValue;
                    }
                }
                return $"= {val:G}";
            }
        }

        private static bool ActionNeedsNumbers(Card card) =>
            card is IPairableCard;

        private static bool ActionNeedsTarget(Card card) =>
            card is ITargetableCard;

        protected bool IsPlayerTargetable(string targetPlayerId, OperatorPlayerState targetState)
        {
            if (!IsMyTurn) return false;

            if (_selectedOperatorId != null && CurrentPlayerState != null)
            {
                var opCard = CurrentPlayerState.Hand.FirstOrDefault(c => c.Id == _selectedOperatorId) as KnockBox.Operator.Models.OperatorCard;
                if (opCard != null && opCard.OperatorValue == targetState.ActiveOperator)
                {
                    return false;
                }
            }

            return true;
        }

        protected Task ToggleCard(Guid cardId)
        {
            if (!IsMyTurn || CurrentPlayerState == null) return Task.CompletedTask;

            var card = CurrentPlayerState.Hand.FirstOrDefault(c => c.Id == cardId);
            if (card == null) return Task.CompletedTask;

            switch (card)
            {
                case NumberCard:
                    if (_waitingForTarget) return Task.CompletedTask;
                    if (_selectedNumberIds.Contains(card.Id))
                        _selectedNumberIds.Remove(card.Id);
                    else
                        _selectedNumberIds.Add(card.Id);
                    break;

                case KnockBox.Operator.Models.OperatorCard:
                    _selectedOperatorId = _selectedOperatorId == card.Id ? null : card.Id;
                    break;

                case ActionCard:
                    HandleActionClick(card);
                    break;
            }

            return Task.CompletedTask;
        }

        private void HandleActionClick(Card card)
        {
            if (card is ShieldCard) return;

            if (_pendingAction?.Id == card.Id)
            {
                _pendingAction = null;
                _waitingForTarget = false;
                return;
            }

            _pendingAction = card;

            if (ActionNeedsNumbers(card))
            {
                _waitingForTarget = false;
                // Clear target for self-only actions (e.g. CookTheBooks)
                if (!ActionNeedsTarget(card))
                    _targetPlayerId = null;
            }
            else if (ActionNeedsTarget(card))
            {
                _waitingForTarget = _targetPlayerId == null;
            }
            else
            {
                // Comp, MarketCrash — no target or numbers needed, just select it
                _waitingForTarget = false;
                _targetPlayerId = null;
            }
        }

        protected void SelectTarget(string playerId)
        {
            if (!IsMyTurn) return;

            // Block target selection for self-only actions (e.g. CookTheBooks, Comp, MarketCrash)
            if (_pendingAction != null && !ActionNeedsTarget(_pendingAction))
                return;

            _targetPlayerId = _targetPlayerId == playerId ? null : playerId;

            if (_waitingForTarget && _targetPlayerId != null)
                _waitingForTarget = false;

            StateHasChanged();
        }

        protected async Task Submit()
        {
            if (!IsMyTurn || UserService.CurrentUser == null) return;

            var cardIds = new List<Guid>(_selectedNumberIds);
            if (_pendingAction != null)
                cardIds.Add(_pendingAction.Id);
            if (_selectedOperatorId != null)
                cardIds.Add(_selectedOperatorId.Value);

            if (cardIds.Count == 0) return;

            await PlayCardSet(cardIds);
        }

        private async Task PlayCardSet(List<Guid> cardIds)
        {
            if (UserService.CurrentUser == null) return;

            var command = new PlayCardsCommand(UserService.CurrentUser.Id, cardIds, _targetPlayerId);
            var result = await GameEngine.ExecuteCommandAsync(GameState, command);

            if (result.TryGetFailure(out var error))
            {
                await OnError.InvokeAsync(error.PublicMessage);
                Logger.LogError("Failed to play cards: {Error}", error);
            }
            else
            {
                ClearSelection();
            }
        }

        private void ClearSelection()
        {
            _pendingAction = null;
            _selectedOperatorId = null;
            _selectedNumberIds.Clear();
            _targetPlayerId = null;
            _waitingForTarget = false;
        }

        protected void CancelSelection()
        {
            ClearSelection();
            StateHasChanged();
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
            if (!IsMyTurn || CurrentPlayerState == null || GameState.Context == null) return false;
            return !CurrentPlayerState.Hand.Any(c => c.IsPlayable(GameState.Context, CurrentPlayerState));
        }

        protected bool CanEndTurn()
        {
            if (!IsMyTurn || CurrentPlayerState == null) return false;
            return CurrentPlayerState.HasPlayedCardThisTurn && CurrentPlayerState.Hand.Count <= 5;
        }

        protected async Task EndTurn()
        {
            if (!IsMyTurn || UserService.CurrentUser == null || !CanEndTurn()) return;

            var command = new EndTurnCommand(UserService.CurrentUser.Id);
            var result = await GameEngine.ExecuteCommandAsync(GameState, command);

            if (result.TryGetFailure(out var error))
            {
                await OnError.InvokeAsync(error.PublicMessage);
                Logger.LogError("Failed to end turn: {Error}", error);
            }
        }

        protected int GetDeckPercentage()
        {
            var total = GameState.Deck.Count + GameState.DiscardPile.Count;
            return total > 0 ? (int)(100.0 * GameState.Deck.Count / total) : 0;
        }

        protected string GetActiveOperatorSymbol() => CurrentPlayerState?.ActiveOperator switch
        {
            CardOperator.Add => "+",
            CardOperator.Subtract => "-",
            CardOperator.Multiply => "\u00d7",
            CardOperator.Divide => "\u00f7",
            _ => "?"
        };
    }
}
