namespace KnockBox.Operator.Models;

public record CardPlayResult(bool ConsumedNumbers = false, bool Toggled = false, string? OperatorTargetId = null)
{
    public static CardPlayResult Ok() => new();
    public static CardPlayResult OkConsumedNumbers() => new(ConsumedNumbers: true);
}
