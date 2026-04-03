using System;
using System.Collections.Generic;

namespace KnockBox.Operator.Models;

public class OperatorPlayerState
{
    public string UserId { get; set; } = string.Empty;
    public decimal CurrentPoints { get; set; } = 0m;
    public CardOperator ActiveOperator { get; set; } = CardOperator.Add;
    public List<Card> Hand { get; set; } = new();
    public bool IsAudited { get; set; }
    public int AuditExpiresTurnCount { get; set; }
    public DateTimeOffset ScoreTimestamp { get; set; } = DateTimeOffset.UtcNow;
}
