using System;

namespace KnockBox.Operator.Models;

public readonly record struct PlayerReaction(Guid PlayerId, Card? ReactionCard);
