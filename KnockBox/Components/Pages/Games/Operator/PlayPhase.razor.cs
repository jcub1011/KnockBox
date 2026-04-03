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
                if (_pendingAction != null) ids.Add(_pendingAction.Value.Id);
                if (_selectedOperatorId != null) ids.Add(_selectedOperatorId.Value);
                return ids;
            }
        }

        protected bool ShowSubmit =>
            _selectedNumberIds.Count > 0
            || (_selectedOperatorId != null && _targetPlayerId != null)
            || (_pendingAction != null && !ActionNeedsNumbers(_pendingAction.Value.ActionValue)
                && (!ActionNeedsTarget(_pendingAction.Value.ActionValue) || _targetPlayerId != null));

        protected bool NeedsTargetSelection =>
            _waitingForTarget
            || (_pendingAction != null && ActionNeedsTarget(_pendingAction.Value.ActionValue) && _targetPlayerId == null)
            || (_selectedOperatorId != null && _targetPlayerId == null);

        protected HashSet<Guid> DisabledCardIds
        {
            get
            {
                if (CurrentPlayerState == null || !IsMyTurn)
                    return CurrentPlayerState?.Hand.Select(c => c.Id).ToHashSet() ?? new();

                var hand = CurrentPlayerState.Hand;
                var disabled = new HashSet<Guid>();

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
                    foreach (var c in hand.Where(c => c.Type != CardType.Number && c.Id != _pendingAction.Value.Id))
                        disabled.Add(c.Id);
                    return disabled;
                }

                // Numbers selected, no action — numbers + actions needing numbers enabled
                if (_selectedNumberIds.Count > 0)
                {
                    foreach (var c in hand)
                    {
                        if (c.Type == CardType.Number) continue;
                        if (c.Type == CardType.Action && ActionNeedsNumbers(c.ActionValue)) continue;
                        disabled.Add(c.Id);
                    }
                    return disabled;
                }

                // No selection — all enabled except Shield
                foreach (var c in hand.Where(c => c.Type == CardType.Action && c.ActionValue == CardAction.Shield))
                    disabled.Add(c.Id);

                return disabled;
            }
        }

        protected string? SelectionHint
        {
            get
            {
                if (_waitingForTarget)
                {
                    var actionName = _pendingAction?.ActionValue switch
                    {
                        CardAction.Steal => "Steal",
                        CardAction.FlashFlood => "Flash Flood",
                        CardAction.HostileTakeover => "Hostile Takeover",
                        CardAction.Audit => "Audit",
                        CardAction.LiabilityTransfer => "Liability Transfer",
                        CardAction.HotPotato => "Hot Potato",
                        _ => "this action"
                    };
                    return $"Select a target for {actionName}";
                }
                if (_selectedOperatorId != null && _targetPlayerId == null)
                    return "Select a target player";
                if (_pendingAction != null && _selectedNumberIds.Count == 0)
                {
                    if (ActionNeedsTarget(_pendingAction.Value.ActionValue))
                        return "Select a target and number cards, then submit";
                    return "Select number cards, then submit";
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
                    if (card.Id == Guid.Empty) continue;
                    val = val * 10 + card.NumberValue;
                }
                return $"= {val:G}";
            }
        }

        private static bool ActionNeedsNumbers(CardAction action) =>
            action is CardAction.CookTheBooks or CardAction.HotPotato or CardAction.LiabilityTransfer;

        private static bool ActionNeedsTarget(CardAction action) =>
            action is CardAction.LiabilityTransfer or CardAction.Steal or CardAction.HotPotato
                or CardAction.FlashFlood or CardAction.HostileTakeover or CardAction.Audit;

        protected Task ToggleCard(Guid cardId)
        {
            if (!IsMyTurn || CurrentPlayerState == null) return Task.CompletedTask;

            var card = CurrentPlayerState.Hand.FirstOrDefault(c => c.Id == cardId);
            if (card.Id == Guid.Empty) return Task.CompletedTask;

            switch (card.Type)
            {
                case CardType.Number:
                    if (_waitingForTarget) return Task.CompletedTask;
                    if (_selectedNumberIds.Contains(card.Id))
                        _selectedNumberIds.Remove(card.Id);
                    else
                        _selectedNumberIds.Add(card.Id);
                    break;

                case CardType.Operator:
                    _selectedOperatorId = _selectedOperatorId == card.Id ? null : card.Id;
                    break;

                case CardType.Action:
                    HandleActionClick(card);
                    break;
            }

            return Task.CompletedTask;
        }

        private void HandleActionClick(Card card)
        {
            if (card.ActionValue == CardAction.Shield) return;

            if (_pendingAction?.Id == card.Id)
            {
                _pendingAction = null;
                _waitingForTarget = false;
                return;
            }

            _pendingAction = card;

            if (ActionNeedsNumbers(card.ActionValue))
            {
                _waitingForTarget = false;
            }
            else if (ActionNeedsTarget(card.ActionValue))
            {
                _waitingForTarget = _targetPlayerId == null;
            }
            else
            {
                // Comp, MarketCrash — no target or numbers needed, just select it
                _waitingForTarget = false;
            }
        }

        protected void SelectTarget(string playerId)
        {
            if (!IsMyTurn) return;

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
                cardIds.Add(_pendingAction.Value.Id);
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
            if (!IsMyTurn || CurrentPlayerState == null) return false;
            return CurrentPlayerState.Hand.All(c => c.Type == CardType.Action && c.ActionValue == CardAction.Shield);
        }

        protected int GetDeckPercentage()
        {
            var total = GameState.Deck.Count + GameState.DiscardPile.Count;
            return total > 0 ? (int)(100.0 * GameState.Deck.Count / total) : 0;
        }
    }
}
