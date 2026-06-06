using System;
using System.Collections.Generic;

namespace KnockBox.Operator.Services.Logic.FSM.Commands;

public record PlayCardsCommand(Guid PlayerId, List<Guid> CardIds, Guid? TargetPlayerId = null) : OperatorCommand(PlayerId);
