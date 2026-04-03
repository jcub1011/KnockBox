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

        private Card? _pendingAction;
        private readonly List<Guid> _selectedNumberIds = new();
        private string? _targetPlayerId;
        private bool _waitingForTarget;

        protected OperatorPlayerState? CurrentPlayerState =>
            UserService.CurrentUser != null ? GameState.Context?.GamePlayers.GetValueOrDefault(UserService.CurrentUser.Id) : null;

        protected bool IsMyTurn => GameState.TurnManager.CurrentPlayer == UserService.CurrentUser?.Id;

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
                return ids;
            }
        }

        protected bool ShowSubmit => _selectedNumberIds.Count > 0;

        protected string? SelectionHint
        {
            get
            {
                if (_waitingForTarget) return "Select a target player";
                if (_pendingAction != null && _selectedNumberIds.Count == 0) return "Select number cards, then submit";
                return null;
            }
        }

        private static bool ActionNeedsNumbers(CardAction action) =>
            action is CardAction.CookTheBooks or CardAction.HotPotato or CardAction.LiabilityTransfer;

        private static bool ActionNeedsTarget(CardAction action) =>
            action is CardAction.LiabilityTransfer or CardAction.Steal or CardAction.HotPotato
                or CardAction.FlashFlood or CardAction.HostileTakeover or CardAction.Audit;

        protected async Task ToggleCard(Guid cardId)
        {
            if (!IsMyTurn || CurrentPlayerState == null) return;

            var card = CurrentPlayerState.Hand.FirstOrDefault(c => c.Id == cardId);
            if (card.Id == Guid.Empty) return;

            switch (card.Type)
            {
                case CardType.Number:
                    if (_waitingForTarget) return;
                    if (_selectedNumberIds.Contains(card.Id))
                        _selectedNumberIds.Remove(card.Id);
                    else
                        _selectedNumberIds.Add(card.Id);
                    break;

                case CardType.Operator:
                    await PlayCardSet(new List<Guid> { card.Id });
                    break;

                case CardType.Action:
                    await HandleActionClick(card);
                    break;
            }
        }

        private async Task HandleActionClick(Card card)
        {
            if (card.ActionValue == CardAction.Shield) return;

            if (ActionNeedsNumbers(card.ActionValue))
            {
                if (_pendingAction?.Id == card.Id)
                {
                    _pendingAction = null;
                }
                else
                {
                    _pendingAction = card;
                    _waitingForTarget = false;
                }
            }
            else if (ActionNeedsTarget(card.ActionValue) && _targetPlayerId == null)
            {
                _pendingAction = card;
                _waitingForTarget = true;
            }
            else
            {
                await PlayCardSet(new List<Guid> { card.Id });
            }
        }

        protected async Task SelectTarget(string playerId)
        {
            if (!IsMyTurn) return;

            _targetPlayerId = _targetPlayerId == playerId ? null : playerId;

            if (_waitingForTarget && _targetPlayerId != null && _pendingAction != null
                && !ActionNeedsNumbers(_pendingAction.Value.ActionValue))
            {
                await PlayCardSet(new List<Guid> { _pendingAction.Value.Id });
                return;
            }

            StateHasChanged();
        }

        protected async Task Submit()
        {
            if (!IsMyTurn || UserService.CurrentUser == null) return;

            var cardIds = new List<Guid>(_selectedNumberIds);
            if (_pendingAction != null)
                cardIds.Add(_pendingAction.Value.Id);

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
    }
}
