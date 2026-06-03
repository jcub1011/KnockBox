using System;
using KnockBox.Operator.Services.Logic.FSM;

namespace KnockBox.Operator.Models;

public record CardPlayContext(
    OperatorGameContext GameContext,
    OperatorPlayerState ThisPlayer,
    Guid? TargetPlayerId,
    decimal CombinedNumberValue,
    List<NumberCard> PairedNumbers,
    bool ActionBlocked
);
