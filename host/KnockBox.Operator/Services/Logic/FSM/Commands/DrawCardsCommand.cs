using System;

namespace KnockBox.Operator.Services.Logic.FSM.Commands;

public record DrawCardsCommand(Guid PlayerId) : OperatorCommand(PlayerId);
