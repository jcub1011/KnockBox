namespace KnockBox.Operator.Models;

public sealed class NumberCard(decimal numberValue = 0m) : Card
{
    public override CardType Type => CardType.Number;

    public decimal NumberValue { get; init; } = numberValue;

    public override string CardIcon()
        => $"{NumberValue:N0}";

    public override string TooltipName()
        => $"Number {NumberValue:N0}";

    public override string TooltipDescription()
        => "Play number cards to modify scores. Stack multiple numbers to form larger values (e.g. 3 + 7 = 37).";
}
