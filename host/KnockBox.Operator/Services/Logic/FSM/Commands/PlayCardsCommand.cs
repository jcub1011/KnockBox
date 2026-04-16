using System;
using System.Collections.Generic;

namespace KnockBox.Operator.Services.Logic.FSM.Commands;

public record PlayCardsCommand(string PlayerId, List<Guid> CardIds, string? TargetPlayerId = null) : OperatorCommand(PlayerId);
