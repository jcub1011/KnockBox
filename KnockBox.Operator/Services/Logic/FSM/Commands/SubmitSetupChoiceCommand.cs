using System;

namespace KnockBox.Operator.Services.Logic.FSM.Commands;

public record SubmitSetupChoiceCommand(string PlayerId, decimal Choice) : OperatorCommand(PlayerId);
