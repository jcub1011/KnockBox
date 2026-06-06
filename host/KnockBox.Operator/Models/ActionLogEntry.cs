using System;

namespace KnockBox.Operator.Models;

public record ActionLogEntry(
    string Message,
    DateTimeOffset Timestamp,
    Guid? SourcePlayerId = null,
    Guid? TargetPlayerId = null,
    Guid? CardId = null);
