using KnockBox.Operator.Services.Logic.FSM;

namespace KnockBox.Operator.Models;

public record CardPlayContext(
    OperatorGameContext GameContext,
    OperatorPlayerState ThisPlayer,
    string? TargetPlayerId,
    decimal CombinedNumberValue,
    List<NumberCard> PairedNumbers,
    bool ActionBlocked
);
