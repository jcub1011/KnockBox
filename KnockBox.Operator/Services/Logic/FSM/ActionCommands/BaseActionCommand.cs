using System.Collections.Generic;
using KnockBox.Operator.Models;
using KnockBox.Operator.Services.Logic.FSM.Commands;

namespace KnockBox.Operator.Services.Logic.FSM.ActionCommands;

public abstract class BaseActionCommand(
    OperatorGameContext context,
    PlayCardsCommand playCommand,
    List<Card> playedCards)
    : IGameActionCommand
{
    protected OperatorGameContext Context { get; } = context;
    protected PlayCardsCommand PlayCommand { get; private set; } = playCommand;
    
    public IEnumerable<Card> PlayedCards { get; } = playedCards;

    public string InitiatorPlayerId => PlayCommand.PlayerId;

    public Card? PrimaryCard => PlayedCards.FirstOrDefault(c => c is ActionCard || c is KnockBox.Operator.Models.OperatorCard);

    public virtual bool RequiresReaction => false;

    public virtual IEnumerable<string> GetReactionTargetIds() => [];

    public virtual void SetupPendingState() { }

    public abstract void Execute();

    public virtual void OnBlocked() { }

    public virtual void UpdateTarget(string newTargetId)
    {
        PlayCommand = PlayCommand with { TargetPlayerId = newTargetId };
    }
}
