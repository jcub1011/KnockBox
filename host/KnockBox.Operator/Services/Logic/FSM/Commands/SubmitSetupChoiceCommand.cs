using System;

namespace KnockBox.Operator.Services.Logic.FSM.Commands;

public record SubmitSetupChoiceCommand(Guid PlayerId, decimal Choice) : OperatorCommand(PlayerId);
