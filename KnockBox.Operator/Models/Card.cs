using System;

namespace KnockBox.Operator.Models;

public enum CardType
{
    Number,
    Operator,
    Action
}

public enum CardOperator
{
    None,
    Add,
    Subtract,
    Multiply,
    Divide
}

public enum CardAction
{
    None,
    Shield,
    LiabilityTransfer
}

public readonly record struct Card(
    CardType Type,
    decimal NumberValue = 0m, // Ignored when card type is not Number
    CardOperator OperatorValue = CardOperator.None,
    CardAction ActionValue = CardAction.None)
{
    public Guid Id { get; init; } = Guid.NewGuid();
}
