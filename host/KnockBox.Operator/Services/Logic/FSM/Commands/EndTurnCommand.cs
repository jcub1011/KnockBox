using System;

namespace KnockBox.Operator.Services.Logic.FSM.Commands;

public record EndTurnCommand(string PlayerId) : OperatorCommand(PlayerId);
