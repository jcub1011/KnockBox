using System;

namespace KnockBox.Operator.Models;

public record CardPlayResult(bool ConsumedNumbers = false, bool Toggled = false, Guid? OperatorTargetId = null)
{
    public static CardPlayResult Ok() => new();
    public static CardPlayResult OkConsumedNumbers() => new(ConsumedNumbers: true);
}
