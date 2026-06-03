using System;

namespace KnockBox.Operator.Services.Logic.FSM.Commands;

public record SkipTurnCommand(Guid PlayerId) : OperatorCommand(PlayerId);
