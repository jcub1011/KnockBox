using System;
using System.Collections.Generic;
using System.Linq;
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

    public Guid InitiatorPlayerId => PlayCommand.PlayerId;

    public Card? PrimaryCard => PlayedCards.FirstOrDefault(c => c is ActionCard || c is OperatorCard);

    public virtual bool RequiresReaction => false;

    public virtual IEnumerable<Guid> GetReactionTargetIds() => [];

    public virtual void SetupPendingState() { }

    public abstract void Execute();

    public virtual void OnBlocked() { }

    public virtual void UpdateTarget(Guid newTargetId)
    {
        PlayCommand = PlayCommand with { TargetPlayerId = newTargetId };
    }

    protected decimal CalculateNumberValue()
    {
        decimal val = 0;
        var numbers = PlayedCards.OfType<NumberCard>().ToList();
        foreach (var num in numbers)
        {
            val = val * 10 + num.NumberValue;
        }
        return val;
    }

    protected void LogPlay(bool actionBlocked)
    {
        string sourceName = GetPlayerName(PlayCommand.PlayerId);
        string targetName = PlayCommand.TargetPlayerId != null ? GetPlayerName(PlayCommand.TargetPlayerId.Value) : "themselves";
        string cardNames = string.Join(", ", PlayedCards.Select(c => c.TooltipName()));

        Context.State.ActionLog.Add(new ActionLogEntry(
            $"{sourceName} played {cardNames} targeting {targetName}",
            DateTimeOffset.UtcNow,
            PlayCommand.PlayerId,
            PlayCommand.TargetPlayerId));

        if (actionBlocked)
        {
            Context.State.ActionLog.Add(new ActionLogEntry(
                $"{targetName} blocked the action!",
                DateTimeOffset.UtcNow,
                PlayCommand.TargetPlayerId,
                PlayCommand.PlayerId));
        }
    }

    protected string GetPlayerName(Guid playerId)
    {
        return Context.State.Participants.FirstOrDefault(p => p.User.Id == playerId).DisplayName ?? "Unknown";
    }
}
