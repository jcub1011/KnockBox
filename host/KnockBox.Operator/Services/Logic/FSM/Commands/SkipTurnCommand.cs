using System;

namespace KnockBox.Operator.Services.Logic.FSM.Commands;

public record SkipTurnCommand(string PlayerId) : OperatorCommand(PlayerId);
