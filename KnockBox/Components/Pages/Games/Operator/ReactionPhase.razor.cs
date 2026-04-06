using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM.Commands;
using KnockBox.Services.Logic.Games.Operator;
using KnockBox.Operator.Services.State;
using KnockBox.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.Components.Pages.Games.Operator
{
    public partial class ReactionPhase : ComponentBase
    {
        [Inject] protected OperatorGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<ReactionPhase> Logger { get; set; } = default!;

        [Parameter] public OperatorGameState GameState { get; set; } = default!;

        [Parameter] public EventCallback<string> OnError { get; set; }

        private Guid? _redirectingWithCardId;

        protected OperatorPlayerState? CurrentPlayerState =>
            UserService.CurrentUser != null ? GameState.Context?.GamePlayers.GetValueOrDefault(UserService.CurrentUser.Id) : null;

        protected bool IsTargeted => GameState.ReactionTargetPlayerId == UserService.CurrentUser?.Id;

        protected bool CanRedirectHotPotato => GameState.PendingHotPotatoCard != null;

        protected async Task PlayShield(Guid cardId)
        {
            if (!IsTargeted || UserService.CurrentUser == null) return;

            var command = new PlayReactionCommand(UserService.CurrentUser.Id, cardId);
            var result = await GameEngine.ExecuteCommandAsync(GameState, command);

            if (result.TryGetFailure(out var error))
            {
                await OnError.InvokeAsync(error.PublicMessage);
                Logger.LogError("Failed to play shield: {Error}", error);
            }
        }

        protected async Task Pass()
        {
            if (!IsTargeted || UserService.CurrentUser == null) return;

            var command = new PassReactionCommand(UserService.CurrentUser.Id);
            var result = await GameEngine.ExecuteCommandAsync(GameState, command);

            if (result.TryGetFailure(out var error))
            {
                await OnError.InvokeAsync(error.PublicMessage);
                Logger.LogError("Failed to pass reaction: {Error}", error);
            }
        }

        protected void StartRedirect(Guid hotPotatoCardId)
        {
            _redirectingWithCardId = hotPotatoCardId;
        }

        protected void CancelRedirect()
        {
            _redirectingWithCardId = null;
        }

        protected async Task RedirectHotPotato(string targetPlayerId)
        {
            if (!IsTargeted || UserService.CurrentUser == null || _redirectingWithCardId == null) return;

            var command = new RedirectHotPotatoCommand(UserService.CurrentUser.Id, _redirectingWithCardId.Value, targetPlayerId);
            var result = await GameEngine.ExecuteCommandAsync(GameState, command);

            _redirectingWithCardId = null;

            if (result.TryGetFailure(out var error))
            {
                await OnError.InvokeAsync(error.PublicMessage);
                Logger.LogError("Failed to redirect hot potato: {Error}", error);
            }
        }

        protected Card? GetPendingActionCard()
        {
            if (GameState.PendingActionCommand is PlayCardsCommand play)
            {
                foreach (var cardId in play.CardIds)
                {
                    var card = GameState.DiscardPile.FirstOrDefault(c => c.Id == cardId);
                    if (card is ActionCard || card is KnockBox.Operator.Models.OperatorCard)
                    {
                        return card;
                    }
                }
            }
            return null;
        }

        protected List<Card> GetPotentialReactionCards()
        {
            if (GameState.Context == null || CurrentPlayerState == null) return new();

            var pendingCard = GetPendingActionCard();
            if (pendingCard is IBlockableCard blockable)
            {
                return blockable.GetPotentialReactionCards(GameState.Context, CurrentPlayerState).ToList();
            }
            return new();
        }

        protected string GetAttackerName()
        {
            if (GameState.PendingActionCommand is PlayCardsCommand play)
            {
                return GameState.Players.FirstOrDefault(p => p.Id == play.PlayerId)?.Name ?? "Unknown";
            }
            return "Unknown";
        }

        protected string GetActionDescription()
        {
            var pendingCard = GetPendingActionCard();
            if (pendingCard != null)
            {
                if (pendingCard is HotPotatoCard)
                {
                    if (GameState.PendingActionCommand is PlayCardsCommand playCmd)
                    {
                        decimal val = 0;
                        var numCards = playCmd.CardIds
                            .Select(id => GameState.DiscardPile.FirstOrDefault(c => c.Id == id))
                            .OfType<NumberCard>()
                            .ToList();

                        if (numCards.Count > 0)
                        {
                            foreach (var num in numCards) val = val * 10 + num.NumberValue;
                            return $"Hot Potato with the value {val}";
                        }
                    }
                }
                if (pendingCard is KnockBox.Operator.Models.OperatorCard opCard)
                {
                    return $"an Operator card to change your operator to {opCard.OperatorValue}";
                }
                return pendingCard.TooltipName();
            }
            return "an action";
        }
    }
}
