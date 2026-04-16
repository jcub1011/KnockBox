using System;

namespace KnockBox.Operator.Models;

public record ActionLogEntry(
    string Message, 
    DateTimeOffset Timestamp,
    string? SourcePlayerId = null,
    string? TargetPlayerId = null,
    Guid? CardId = null);
