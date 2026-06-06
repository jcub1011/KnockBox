using System;

namespace KnockBox.Operator.Services.Logic.FSM.Commands;

public record EndTurnCommand(Guid PlayerId) : OperatorCommand(PlayerId);
